using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MpvShell.Player.LibMpv.Tests;

/// <summary>
/// 仅绑定本机随机端口的测试 HLS 服务：一份 VOD 播放列表加若干打包 AAC（ADTS）分片，全部在内存里生成，
/// 不依赖外部工具或公网地址。分片是同一段静音 AAC-LC 帧的重复，HLS 规范与 FFmpeg 的 hls 解复用器都支持
/// 无 MPEG-TS 封装的打包音频分片。
/// </summary>
internal sealed class HlsMediaServer : IAsyncDisposable
{
    // ffmpeg 8.1（libfdk 不参与）对 anullsrc 单声道 44.1 kHz 编码出的一帧静音 AAC-LC：7 字节 ADTS 头 + 4 字节负载，
    // 头部 frame_length 字段为 11，每帧 1024 采样。
    private static readonly byte[] SilentAdtsFrame = [0xFF, 0xF1, 0x50, 0x40, 0x01, 0x7F, 0xFC, 0x01, 0x18, 0x20, 0x07];
    private const int SampleRate = 44100;
    private const int SamplesPerFrame = 1024;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentBag<Task> _clients = [];
    private readonly Dictionary<string, (string ContentType, byte[] Body)> _resources = new(StringComparer.Ordinal);
    private readonly Task _acceptLoop;

    internal ConcurrentQueue<string> Requests { get; } = new();
    internal ConcurrentQueue<int> ResponseCodes { get; } = new();
    internal string PlaylistUrl { get; }
    internal string MissingPlaylistUrl { get; }
    internal string BrokenSegmentPlaylistUrl { get; }
    internal int SegmentCount { get; }
    internal double SegmentSeconds { get; }
    internal double TotalSeconds { get; }

    internal HlsMediaServer(int segmentCount = 4, int framesPerSegment = 86)
    {
        SegmentCount = segmentCount;
        SegmentSeconds = framesPerSegment * (double)SamplesPerFrame / SampleRate;
        TotalSeconds = SegmentSeconds * segmentCount;
        var segment = new byte[SilentAdtsFrame.Length * framesPerSegment];
        for (var frame = 0; frame < framesPerSegment; frame++)
            SilentAdtsFrame.CopyTo(segment, frame * SilentAdtsFrame.Length);

        var playlist = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n");
        playlist.Append("#EXT-X-TARGETDURATION:").Append(Math.Ceiling(SegmentSeconds).ToString(CultureInfo.InvariantCulture)).Append('\n');
        playlist.Append("#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n");
        var broken = new StringBuilder(playlist.ToString());
        for (var index = 0; index < segmentCount; index++)
        {
            // 播放列表里的相对地址按播放列表所在目录（/live/）解析。
            var name = $"/live/segments/part{index}.aac";
            _resources[name] = ("audio/aac", segment);
            playlist.Append("#EXTINF:").Append(SegmentSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(",\n")
                .Append("segments/part").Append(index).Append(".aac\n");
            broken.Append("#EXTINF:").Append(SegmentSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(",\n")
                .Append("segments/").Append(index == 0 ? "does-not-exist" : $"part{index}").Append(".aac\n");
        }
        playlist.Append("#EXT-X-ENDLIST\n");
        broken.Append("#EXT-X-ENDLIST\n");
        _resources["/live/index.m3u8"] = ("application/vnd.apple.mpegurl", Encoding.ASCII.GetBytes(playlist.ToString()));
        _resources["/live/broken.m3u8"] = ("application/vnd.apple.mpegurl", Encoding.ASCII.GetBytes(broken.ToString()));

        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        PlaylistUrl = $"http://127.0.0.1:{port}/live/index.m3u8?token=private-hls-token";
        MissingPlaylistUrl = $"http://127.0.0.1:{port}/live/missing.m3u8";
        BrokenSegmentPlaylistUrl = $"http://127.0.0.1:{port}/live/broken.m3u8";
        _acceptLoop = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                _clients.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (SocketException) when (_shutdown.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                // FFmpeg 的 HTTP 客户端可以在同一连接上顺序请求多个分片。
                while (!_shutdown.IsCancellationRequested)
                {
                    var request = await reader.ReadLineAsync(_shutdown.Token);
                    if (request is null) return;
                    var parts = request.Split(' ');
                    if (parts.Length < 2) return;
                    string? range = null;
                    var keepAlive = !request.EndsWith("HTTP/1.0", StringComparison.Ordinal);
                    for (var count = 0; count < 128; count++)
                    {
                        var line = await reader.ReadLineAsync(_shutdown.Token);
                        if (string.IsNullOrEmpty(line)) break;
                        if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = line[6..].Trim();
                        if (line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase) && line.Contains("close", StringComparison.OrdinalIgnoreCase)) keepAlive = false;
                    }

                    var path = parts[1].Split('?')[0];
                    Requests.Enqueue(path);
                    if (!_resources.TryGetValue(path, out var resource))
                    {
                        await WriteResponseAsync(stream, 404, "Not Found", "text/plain", [], 0, 0, false, true, keepAlive);
                        if (!keepAlive) return;
                        continue;
                    }

                    var body = resource.Body;
                    var start = 0;
                    var end = body.Length - 1;
                    if (range is not null)
                    {
                        var bounds = range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) ? range[6..].Split('-') : [];
                        if (bounds.Length != 2 || !int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out start)
                            || start < 0 || start >= body.Length)
                        {
                            await WriteResponseAsync(stream, 416, "Range Not Satisfiable", "text/plain", [], 0, 0, false, true, keepAlive);
                            if (!keepAlive) return;
                            continue;
                        }
                        if (!string.IsNullOrEmpty(bounds[1]) && int.TryParse(bounds[1], out var requestedEnd))
                            end = Math.Clamp(requestedEnd, start, body.Length - 1);
                    }
                    await WriteResponseAsync(stream, range is null ? 200 : 206, range is null ? "OK" : "Partial Content",
                        resource.ContentType, body, start, end - start + 1, range is not null, parts[0] == "HEAD", keepAlive);
                    if (!keepAlive) return;
                }
            }
            catch (IOException) { /* libmpv seek/停止时可以主动关闭上一条连接。 */ }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (SocketException) { }
        }
    }

    private async Task WriteResponseAsync(NetworkStream stream, int code, string reason, string contentType, byte[] body,
        int offset, int count, bool range, bool headersOnly, bool keepAlive)
    {
        ResponseCodes.Enqueue(code);
        var contentRange = range ? $"Content-Range: bytes {offset}-{offset + count - 1}/{body.Length}\r\n" : "";
        var header = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {count}\r\nAccept-Ranges: bytes\r\n" +
            $"{contentRange}Cache-Control: no-cache\r\nConnection: {(keepAlive ? "keep-alive" : "close")}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), _shutdown.Token);
        if (!headersOnly && count > 0) await stream.WriteAsync(body.AsMemory(offset, count), _shutdown.Token);
        await stream.FlushAsync(_shutdown.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();
        await _acceptLoop;
        await Task.WhenAll(_clients);
        _shutdown.Dispose();
    }
}

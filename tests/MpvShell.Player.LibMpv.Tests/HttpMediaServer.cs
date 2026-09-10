using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MpvShell.Player.LibMpv.Tests;

/// <summary>仅绑定本机随机端口的测试 HTTP 服务，提供可重复生成的 PCM WAV 与字节范围响应。</summary>
internal sealed class HttpMediaServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentBag<Task> _clients = [];
    private readonly byte[] _wave;
    private readonly Task _acceptLoop;

    internal ConcurrentQueue<int> ResponseCodes { get; } = new();
    internal ConcurrentQueue<string> RangeRequests { get; } = new();
    internal string MediaUrl { get; }
    internal string MissingUrl { get; }

    internal HttpMediaServer()
    {
        _wave = CreateWave(8);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        MediaUrl = $"http://127.0.0.1:{port}/media.wav?token=private-test-token";
        MissingUrl = $"http://127.0.0.1:{port}/missing.wav";
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
                var request = await reader.ReadLineAsync(_shutdown.Token);
                if (request is null) return;
                var parts = request.Split(' ');
                if (parts.Length < 2) return;
                string? range = null;
                for (var count = 0; count < 128; count++)
                {
                    var line = await reader.ReadLineAsync(_shutdown.Token);
                    if (string.IsNullOrEmpty(line)) break;
                    if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = line[6..].Trim();
                }

                if (!parts[1].Split('?')[0].Equals("/media.wav", StringComparison.Ordinal))
                {
                    await WriteResponseAsync(stream, 404, "Not Found", 0, 0, false, true);
                    return;
                }

                var start = 0;
                var end = _wave.Length - 1;
                if (range is not null)
                {
                    RangeRequests.Enqueue(range);
                    var bounds = range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) ? range[6..].Split('-') : [];
                    if (bounds.Length != 2 || !int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out start)
                        || start < 0 || start >= _wave.Length)
                    {
                        await WriteResponseAsync(stream, 416, "Range Not Satisfiable", 0, 0, false, true);
                        return;
                    }
                    if (!string.IsNullOrEmpty(bounds[1]) && int.TryParse(bounds[1], out var requestedEnd))
                        end = Math.Clamp(requestedEnd, start, _wave.Length - 1);
                }

                await WriteResponseAsync(stream, range is null ? 200 : 206, range is null ? "OK" : "Partial Content",
                    start, end - start + 1, range is not null, parts[0] == "HEAD");
            }
            catch (IOException) { /* libmpv seek/停止时可以主动关闭上一条连接。 */ }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (SocketException) { }
        }
    }

    private async Task WriteResponseAsync(NetworkStream stream, int code, string reason, int offset, int count, bool range, bool headersOnly)
    {
        ResponseCodes.Enqueue(code);
        var contentRange = range ? $"Content-Range: bytes {offset}-{offset + count - 1}/{_wave.Length}\r\n" : "";
        var header = $"HTTP/1.1 {code} {reason}\r\nContent-Type: audio/wav\r\nContent-Length: {count}\r\nAccept-Ranges: bytes\r\n{contentRange}Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), _shutdown.Token);
        if (!headersOnly && count > 0) await stream.WriteAsync(_wave.AsMemory(offset, count), _shutdown.Token);
    }

    private static byte[] CreateWave(int durationSeconds)
    {
        const int sampleRate = 44100;
        var dataLength = sampleRate * durationSeconds * 2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(dataLength + 36);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
        return stream.ToArray();
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

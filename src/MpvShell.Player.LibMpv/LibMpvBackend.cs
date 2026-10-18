using System.Globalization;
using System.Diagnostics;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv;

public sealed class LibMpvBackend : IPlayerBackend
{
    private readonly MpvPlayerSession _session;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly PlayerEventBuffer _events = new();
    private readonly Dictionary<string, object?> _properties = new(StringComparer.Ordinal);
    private PlaybackState _state = PlaybackState.Initial;
    private IReadOnlyList<TrackInfo> _tracks = Array.Empty<TrackInfo>();
    private InfoPanelSnapshot _info = new(null, null, null, null, null, null, null);
    private bool _paused = true;
    private bool _idle = true;
    private bool _loaded;
    private bool _ended;
    private bool _awaitingStart;
    private long? _pendingEntryId;
    private bool _mediaActive;
    private bool _isBuffering;
    private Exception? _terminalError;
    private TaskCompletionSource? _loadCompletion;
    private int _disposed;
    private long _lastDiagnosticTimestamp;

    public LibMpvBackend(MpvPlayerSession session)
    {
        _session = session;
        _session.EventReceived += OnSessionEvent;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        await _session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            PublishState();
        }
    }

    public async Task LoadUrlAsync(string url, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        var source = ValidateMediaSource(url);
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = true; // 与提交回调在 _stateGate 内协调；等待结束不等于原生加载结束。
        try
        {
            ThrowIfUnavailable();
            await _session.SetPropertyAsync("pause", false, cancellationToken).ConfigureAwait(false);
            ThrowIfUnavailable();
            await _session.CommandAsync(["loadfile", source, "replace"], cancellationToken, () =>
            {
                lock (_stateGate)
                {
                    if (_disposed != 0 || _terminalError is not null) return;
                    Trace.WriteLine($"[libmpv] 加载媒体：{(Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}/[媒体]" : Path.GetFileName(source))}");
                    _state = _state with { CurrentUrl = source };
                    _events.ResetMedia();
                    _loadCompletion = waiting ? completion : null;
                    _awaitingStart = true;
                    _pendingEntryId = null;
                    _mediaActive = false;
                    PublishBuffering(false);
                }
            }).ConfigureAwait(false);
            // loadfile 的命令回复只表示已排队；真正读取媒体头失败必须返回给调用方。
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex) { throw new TimeoutException("加载媒体超时，请检查地址或网络连接。", ex); }
        finally
        {
            lock (_stateGate)
            {
                waiting = false;
                if (ReferenceEquals(_loadCompletion, completion)) _loadCompletion = null;
                // 仅移除调用方等待；已提交加载仍由 StartFile / FileLoaded / EndFile 推进。
            }
            _loadGate.Release();
        }
    }

    internal static string ValidateMediaSource(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 32_768 || input.Contains('\0'))
            throw new ArgumentException("请输入有效的本地媒体路径或 HTTP / HTTPS 直链。", nameof(input));
        var source = input.Trim();
        if (Path.IsPathFullyQualified(source) && File.Exists(source))
            return Path.GetFullPath(source);
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host))
            return uri.AbsoluteUri;
        throw new ArgumentException("仅支持存在的本地媒体文件及 HTTP / HTTPS 直链。", nameof(input));
    }

    public Task PlayAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        string? replay;
        lock (_stateGate)
        {
            if (!_loaded && !_ended)
                throw new InvalidOperationException("请先打开媒体。");
            replay = _ended ? _state.CurrentUrl : null;
        }
        return replay is not null ? LoadUrlAsync(replay, cancellationToken) : SetAsync("pause", false, cancellationToken);
    }
    public Task PauseAsync(CancellationToken cancellationToken) => SetAsync("pause", true, cancellationToken);

    public async Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        if (!double.IsFinite(deltaSeconds)) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        double? fromEnd;
        lock (_stateGate) fromEnd = _ended && !_loaded ? Math.Max(0, _state.PositionSeconds + deltaSeconds) : null;
        if (fromEnd is double target)
            await SetPositionAsync(target, cancellationToken).ConfigureAwait(false);
        else
            await _session.CommandAsync(["seek", deltaSeconds.ToString("R", CultureInfo.InvariantCulture), "relative+exact"], cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        if (!double.IsFinite(absoluteSeconds) || absoluteSeconds < 0) throw new ArgumentOutOfRangeException(nameof(absoluteSeconds));
        string? replay;
        lock (_stateGate) replay = _ended && !_loaded ? _state.CurrentUrl : null;
        if (replay is not null) await LoadUrlAsync(replay, cancellationToken).ConfigureAwait(false);
        await _session.CommandAsync(["seek", absoluteSeconds.ToString("R", CultureInfo.InvariantCulture), "absolute+exact"], cancellationToken).ConfigureAwait(false);
    }

    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => SetAsync("volume", Math.Clamp(volume, 0, 100), cancellationToken);
    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken) => SetAsync("mute", muted, cancellationToken);
    public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) => SetAsync("aid", trackId <= 0 ? "no" : trackId, cancellationToken);
    public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) => SetAsync("sid", trackId <= 0 ? "no" : trackId, cancellationToken);

    private Task SetAsync(string property, object value, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        return _session.SetPropertyAsync(property, value, cancellationToken);
    }

    public Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate) return Task.FromResult(_tracks);
    }

    public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate) return Task.FromResult(_info);
    }

    public IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(CancellationToken cancellationToken) =>
        _events.ReadAllAsync(cancellationToken);

    internal void OnSessionEvent(SessionEvent playerEvent)
    {
        lock (_stateGate)
        {
            if (_disposed != 0 || _terminalError is not null) return;
            // 新媒体尚未发出 StartFile 时，忽略排队中的旧媒体状态/结束事件。
            if (_awaitingStart && playerEvent.Id is MpvEventId.EndFile or MpvEventId.FileLoaded)
                return;
            if (playerEvent.Error is not null || playerEvent.Id == MpvEventId.Shutdown)
            {
                var error = playerEvent.Error ?? new InvalidOperationException("播放器会话已关闭。");
                var kind = playerEvent.ErrorKind ?? playerEvent.Id switch
                {
                    MpvEventId.Shutdown => PlayerErrorKind.Session,
                    MpvEventId.QueueOverflow => PlayerErrorKind.EventQueue,
                    MpvEventId.EndFile => _loaded ? PlayerErrorKind.Playback : PlayerErrorKind.Loading,
                    _ => PlayerErrorKind.EventStream,
                };
                var fault = new BackendFaulted(error.Message, kind);
                Trace.WriteLine($"[libmpv] {kind} 错误：{error}");
                _mediaActive = false;
                PublishBuffering(false);
                if (fault.IsTerminal)
                {
                    _terminalError = error;
                    _loaded = false;
                    _ended = false;
                    _idle = true;
                    _awaitingStart = false;
                    _loadCompletion?.TrySetException(error);
                    PublishState();
                    Publish(fault);
                    return;
                }
                Publish(fault);
                if (kind == PlayerErrorKind.EventQueue)
                {
                    // 溢出可能丢失 FileLoaded/EndFile，当前加载等待不能再依赖这些事件。
                    // 允许重新加载；旧 FileLoaded 不能单独恢复播放，必须先看到新 StartFile。
                    _loaded = false;
                    _ended = false;
                    _idle = true;
                    _pendingEntryId = null;
                    _events.ClearEnd();
                    _loadCompletion?.TrySetException(error);
                    PublishState();
                    return;
                }
            }

            switch (playerEvent.Id)
            {
                case MpvEventId.CommandReply when playerEvent.PlaylistEntryId is long entryId:
                    // loadfile 的命令回复携带新条目的原生 playlist_entry_id；先于其 StartFile 更新归属期望。
                    _pendingEntryId = entryId;
                    break;
                case MpvEventId.StartFile:
                    // 归属校验：等待新加载期间只接受与 loadfile 回复 ID 一致的 StartFile。旧媒体的迟到
                    // StartFile 若清除等待标记，紧随其后的旧 EndFile 会把仍在加载的新媒体误判为中止。
                    if (_awaitingStart && playerEvent.PlaylistEntryId != _pendingEntryId) return;
                    _events.ResetMedia();
                    _awaitingStart = false;
                    _loaded = false;
                    _ended = false;
                    _mediaActive = true;
                    PublishBuffering(false);
                    _properties.Clear();
                    _state = _state with { PositionSeconds = 0, DurationSeconds = 0 };
                    _tracks = Array.Empty<TrackInfo>();
                    _info = new(null, null, null, null, null, null, null);
                    Publish(new TracksChanged(_tracks));
                    Publish(new MediaInfoChanged(_info));
                    PublishState();
                    break;
                case MpvEventId.FileLoaded:
                    if (!_mediaActive) break;
                    Trace.WriteLine("[libmpv] 媒体加载完成。");
                    _loaded = true;
                    _idle = false;
                    PublishState();
                    _loadCompletion?.TrySetResult();
                    break;
                case MpvEventId.EndFile:
                    _loaded = false;
                    _mediaActive = false;
                    PublishBuffering(false);
                    // 播放列表展开以 EndFile(redirect) 结束当前条目，随后继续加载插入的媒体。
                    // 只有确有后续条目时才保留等待；空列表必须及时结束，不能等到加载超时。
                    if (!(playerEvent.Value is 5 && playerEvent.PlaylistInsertCount > 0)
                        && _loadCompletion is not null && !_loadCompletion.Task.IsCompleted)
                        _loadCompletion.TrySetException(playerEvent.Error ?? new InvalidOperationException("媒体加载已中止。"));
                    if (playerEvent.Value is 0) MarkEnded();
                    PublishState();
                    LogPlaybackDiagnostics(force: true);
                    break;
                case MpvEventId.PropertyChange when playerEvent.Property is not null:
                    if (_awaitingStart && playerEvent.Property is not ("pause" or "volume" or "mute")) break;
                    // 媒体播放到结尾时 mpv 先卸载视频链，把 video-params、hwdec-current 等逐项清成 null，
                    // 然后才发 EndFile。保留最后一次已知的媒体信息与丢帧计数，直到下一次 StartFile 重置。
                    if (playerEvent.Value is null && IsMediaInfoProperty(playerEvent.Property)
                        && _properties.TryGetValue(playerEvent.Property, out var known) && known is not null)
                        break;
                    _properties[playerEvent.Property] = playerEvent.Value;
                    UpdateProperty(playerEvent.Property, playerEvent.Value);
                    break;
            }
        }
    }

    private static bool IsMediaInfoProperty(string property) => property is "video-params" or "video-codec"
        or "audio-codec-name" or "estimated-vf-fps" or "container-fps" or "cache-buffering-state" or "hwdec-current"
        or "hwdec-interop" or "decoder-frame-drop-count" or "frame-drop-count";

    private void UpdateProperty(string property, object? value)
    {
        switch (property)
        {
            case "pause": _paused = value is true; PublishState(); break;
            case "idle-active": _idle = value is true; PublishState(); break;
            case "eof-reached":
                if (value is true) MarkEnded();
                else if (value is false && _loaded)
                {
                    _ended = false;
                    _events.ClearEnd();
                }
                PublishState(); break;
            case "time-pos" when value is not null:
                _state = _state with { PositionSeconds = Number(value) }; PublishState(); break;
            case "duration" when value is not null:
                _state = _state with { DurationSeconds = Number(value) }; PublishState(); break;
            case "volume" when value is not null:
                _state = _state with { Volume = (int)Math.Round(Number(value)) }; PublishState(); break;
            case "mute": _state = _state with { IsMuted = value is true }; PublishState(); break;
            case "paused-for-cache": PublishBuffering(value is true && _mediaActive && !_ended); break;
            case "track-list":
                _tracks = ParseTracks(value);
                Publish(new TracksChanged(_tracks));
                // EOF 清空整份轨道列表时保留最后媒体信息；视频仍在播放且音轨全部取消选择，
                // 则是当前媒体的音频关闭。无论 codec=null 与 track-list 的送达顺序如何，都清除旧编码。
                if (_loaded && _mediaActive && !_ended && value is object?[] nodes
                    && nodes.OfType<Dictionary<string, object?>>().Any(node =>
                        Text(node.GetValueOrDefault("type")) == "video" && node.GetValueOrDefault("selected") is true)
                    && _tracks.Any(track => track.Kind == "audio")
                    && !_tracks.Any(track => track.Kind == "audio" && track.Selected))
                {
                    _properties["audio-codec-name"] = null;
                    PublishInfo();
                }
                break;
            default:
                PublishInfo();
                break;
        }
        LogPlaybackDiagnostics(force: property is "hwdec-current" or "hwdec-interop" or "video-params"
            or "pause" or "idle-active" or "paused-for-cache" or "video-codec" or "audio-codec-name");
    }

    private void PublishInfo()
    {
        var next = BuildInfo(_properties);
        if (next != _info) { _info = next; Publish(new MediaInfoChanged(_info)); }
    }

    private void LogPlaybackDiagnostics(bool force = false)
    {
        if (!_session.IsDebugLoggingEnabled) return;
        var now = Stopwatch.GetTimestamp();
        if (!force && _lastDiagnosticTimestamp != 0 && Stopwatch.GetElapsedTime(_lastDiagnosticTimestamp, now).TotalSeconds < 5) return;
        _lastDiagnosticTimestamp = now;
        Trace.WriteLine($"[playback] position={_state.PositionSeconds:0.000}s / {_state.DurationSeconds:0.000}s; " +
            $"playing={_state.IsPlaying}; paused={_paused}; buffering={_isBuffering}; loaded={_loaded}; {_info}");
    }

    private void MarkEnded()
    {
        if (_ended) return;
        _ended = true;
        // mpv 的 time-pos 停在最后一帧的时间戳（8 秒视频约 7.97 秒），UI 向下取整会显示 00:07 / 00:08。
        // 播放已经到达结尾，进度按时长对齐；下一次加载由 StartFile 重置为 0。
        if (_state.DurationSeconds > 0)
            _state = _state with { PositionSeconds = _state.DurationSeconds };
        PublishBuffering(false);
        Trace.WriteLine("[libmpv] 播放结束 EOF。");
        Publish(new EndReached());
    }

    private void PublishState()
    {
        _state = _state with { IsPlaying = _loaded && !_paused && !_idle && !_ended };
        Publish(new PlaybackStateChanged(_state));
    }

    private void PublishBuffering(bool isBuffering)
    {
        if (_isBuffering == isBuffering) return;
        _isBuffering = isBuffering;
        Publish(new BufferingChanged(isBuffering));
    }

    private void Publish(PlayerEvent playerEvent) => _events.Publish(playerEvent);

    internal static IReadOnlyList<TrackInfo> ParseTracks(object? value)
    {
        if (value is not object?[] nodes) return Array.Empty<TrackInfo>();
        var result = new List<TrackInfo>();
        foreach (var node in nodes.OfType<Dictionary<string, object?>>())
        {
            var kind = Text(node.GetValueOrDefault("type"));
            if (kind is not ("audio" or "sub")) continue;
            var id = (int)Number(node.GetValueOrDefault("id"));
            var language = Text(node.GetValueOrDefault("lang"));
            var title = Text(node.GetValueOrDefault("title"));
            result.Add(new TrackInfo(id, kind, language,
                title ?? $"{(kind == "audio" ? "音轨" : "字幕")} {id}{(language is null ? "" : $" · {language}")}",
                node.GetValueOrDefault("selected") is true));
        }
        if (result.Any(track => track.Kind == "sub"))
            result.Add(new TrackInfo(0, "sub", null, "关闭字幕", !result.Any(track => track.Kind == "sub" && track.Selected)));
        return result.ToArray();
    }

    internal static InfoPanelSnapshot BuildInfo(IReadOnlyDictionary<string, object?> properties)
    {
        var video = properties.GetValueOrDefault("video-params") as Dictionary<string, object?>;
        var width = Number(video?.GetValueOrDefault("w"));
        var height = Number(video?.GetValueOrDefault("h"));
        var gamma = Text(video?.GetValueOrDefault("gamma"));
        var dynamicRange = gamma switch { "pq" => VideoDynamicRange.Pq, "hlg" => VideoDynamicRange.Hlg, null => VideoDynamicRange.Unknown, _ => VideoDynamicRange.Sdr };
        var hdr = dynamicRange switch { VideoDynamicRange.Pq => "HDR10 / PQ", VideoDynamicRange.Hlg => "HLG", VideoDynamicRange.Sdr => "SDR", _ => null };
        var fps = Number(properties.GetValueOrDefault("estimated-vf-fps"));
        if (fps <= 0) fps = Number(properties.GetValueOrDefault("container-fps"));
        // mpv 0.41 不暴露 plane-depth；硬件帧按底层像素格式识别，未知格式不猜测。
        var bits = PixelBitDepth(Text(video?.GetValueOrDefault("hw-pixelformat")) ?? Text(video?.GetValueOrDefault("pixelformat")));
        var cache = properties.GetValueOrDefault("cache-buffering-state");
        var decoder = Text(properties.GetValueOrDefault("hwdec-current"));
        var decodeMode = decoder switch
        {
            null or "" => VideoDecodeMode.Unknown,
            "no" => VideoDecodeMode.Software,
            "d3d11va" => VideoDecodeMode.D3D11,
            _ when decoder.EndsWith("-copy", StringComparison.Ordinal) => VideoDecodeMode.CopyBack,
            _ => VideoDecodeMode.OtherHardware,
        };
        var diagnostics = new VideoDecodeDiagnostics(decodeMode, decoder,
            Text(properties.GetValueOrDefault("hwdec-interop")), Text(video?.GetValueOrDefault("pixelformat")),
            Text(video?.GetValueOrDefault("hw-pixelformat")),
            Counter(properties.GetValueOrDefault("decoder-frame-drop-count")),
            Counter(properties.GetValueOrDefault("frame-drop-count")));
        return new InfoPanelSnapshot(
            Text(properties.GetValueOrDefault("video-codec")), Text(properties.GetValueOrDefault("audio-codec-name")), hdr,
            width > 0 && height > 0 ? $"{width:0} × {height:0}" : null,
            bits is not null ? $"{bits} bit" : null, fps > 0 ? $"{fps:0.###} fps" : null,
            cache is null ? null : $"缓冲 {Number(cache):0}%", diagnostics, dynamicRange);
    }

    private static string? Text(object? value) => value as string;
    private static int? PixelBitDepth(string? format) => format switch
    {
        "nv12" or "nv21" or "yuv420p" or "yuv422p" or "yuv444p" or "gbrp" or "rgb24" or "bgr24" or "rgba" or "bgra" => 8,
        "p010" or "p010le" or "p010be" or "yuv420p10" or "yuv420p10le" or "yuv420p10be" or
            "yuv422p10" or "yuv422p10le" or "yuv422p10be" or "yuv444p10" or "yuv444p10le" or "yuv444p10be" or "gbrp10" => 10,
        "p012" or "p012le" or "p012be" or "yuv420p12" or "yuv420p12le" or "yuv420p12be" or
            "yuv422p12" or "yuv422p12le" or "yuv422p12be" or "yuv444p12" or "yuv444p12le" or "yuv444p12be" or "gbrp12" => 12,
        "p016" or "p016le" or "p016be" or "yuv420p16" or "yuv420p16le" or "yuv420p16be" or
            "yuv422p16" or "yuv422p16le" or "yuv422p16be" or "yuv444p16" or "yuv444p16le" or "yuv444p16be" or "gbrp16" => 16,
        _ => null,
    };
    private static long? Counter(object? value) => value switch { long count when count >= 0 => count, int count when count >= 0 => count, _ => null };
    private static double Number(object? value) => value switch { double number => number, long integer => integer, int integer => integer, _ => 0 };
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private void ThrowIfUnavailable()
    {
        ThrowIfDisposed();
        lock (_stateGate)
            if (_terminalError is not null)
                throw new InvalidOperationException("播放器已停止，请重新启动。", _terminalError);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _session.EventReceived -= OnSessionEvent;
            lock (_stateGate)
            {
                _loadCompletion?.TrySetException(new ObjectDisposedException(nameof(LibMpvBackend)));
                _mediaActive = false;
                PublishBuffering(false);
            }
            _events.Complete();
        }
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}

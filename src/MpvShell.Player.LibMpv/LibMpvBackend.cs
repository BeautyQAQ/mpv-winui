using System.Globalization;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
    private readonly Channel<PlayerEvent> _events = Channel.CreateBounded<PlayerEvent>(new BoundedChannelOptions(256)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest, AllowSynchronousContinuations = false,
    });
    private readonly Dictionary<string, object?> _properties = new(StringComparer.Ordinal);
    private PlaybackState _state = PlaybackState.Initial;
    private IReadOnlyList<TrackInfo> _tracks = Array.Empty<TrackInfo>();
    private InfoPanelSnapshot _info = new(null, null, null, null, null, null, null);
    private bool _paused = true;
    private bool _idle = true;
    private bool _loaded;
    private bool _ended;
    private bool _awaitingStart;
    private TaskCompletionSource? _loadCompletion;
    private int _disposed;

    public LibMpvBackend(MpvPlayerSession session)
    {
        _session = session;
        _session.EventReceived += OnSessionEvent;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate) PublishState();
    }

    public async Task LoadUrlAsync(string url, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var source = ValidateMediaSource(url);
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Trace.WriteLine($"[libmpv] 加载媒体：{(Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}/[媒体]" : Path.GetFileName(source))}");
            lock (_stateGate)
            {
                _state = _state with { CurrentUrl = source };
                _loadCompletion = completion;
                _awaitingStart = true;
            }
            await _session.SetPropertyAsync("pause", false, cancellationToken).ConfigureAwait(false);
            await _session.CommandAsync(["loadfile", source, "replace"], cancellationToken).ConfigureAwait(false);
            // loadfile 的命令回复只表示已排队；真正读取媒体头失败必须返回给调用方。
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex) { throw new TimeoutException("加载媒体超时，请检查地址或网络连接。", ex); }
        finally
        {
            lock (_stateGate)
            {
                if (ReferenceEquals(_loadCompletion, completion)) _loadCompletion = null;
                _awaitingStart = false;
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
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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

    public async IAsyncEnumerable<PlayerEvent> ObserveEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var playerEvent in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return playerEvent;
    }

    private void OnSessionEvent(SessionEvent playerEvent)
    {
        lock (_stateGate)
        {
            if (_disposed != 0) return;
            // 新媒体尚未发出 StartFile 时，忽略排队中的旧媒体状态/结束事件。
            if (_awaitingStart && playerEvent.Id is MpvEventId.EndFile or MpvEventId.FileLoaded)
                return;
            if (playerEvent.Error is not null)
            {
                Trace.WriteLine($"[libmpv] 播放错误：{playerEvent.Error.Message}");
                Publish(new BackendFaulted(playerEvent.Error.Message));
            }

            switch (playerEvent.Id)
            {
                case MpvEventId.StartFile:
                    _awaitingStart = false;
                    _loaded = false;
                    _ended = false;
                    _properties.Clear();
                    _state = _state with { PositionSeconds = 0, DurationSeconds = 0 };
                    _tracks = Array.Empty<TrackInfo>();
                    _info = new(null, null, null, null, null, null, null);
                    Publish(new TracksChanged(_tracks));
                    Publish(new MediaInfoChanged(_info));
                    PublishState();
                    break;
                case MpvEventId.FileLoaded:
                    Trace.WriteLine("[libmpv] 媒体加载完成。");
                    _loaded = true;
                    _idle = false;
                    PublishState();
                    _loadCompletion?.TrySetResult();
                    break;
                case MpvEventId.EndFile:
                    _loaded = false;
                    if (_loadCompletion is not null && !_loadCompletion.Task.IsCompleted)
                        _loadCompletion.TrySetException(playerEvent.Error ?? new InvalidOperationException("媒体加载已中止。"));
                    if (playerEvent.Value is 0) MarkEnded();
                    PublishState();
                    break;
                case MpvEventId.Shutdown:
                    _loadCompletion?.TrySetException(playerEvent.Error ?? new InvalidOperationException("播放器会话已关闭。"));
                    _loaded = false;
                    _idle = true;
                    PublishState();
                    if (playerEvent.Error is null) Publish(new BackendFaulted("播放器会话已关闭。"));
                    break;
                case MpvEventId.PropertyChange when playerEvent.Property is not null:
                    if (_awaitingStart && playerEvent.Property is not ("pause" or "volume" or "mute")) break;
                    _properties[playerEvent.Property] = playerEvent.Value;
                    UpdateProperty(playerEvent.Property, playerEvent.Value);
                    break;
            }
        }
    }

    private void UpdateProperty(string property, object? value)
    {
        switch (property)
        {
            case "pause": _paused = value is true; PublishState(); break;
            case "idle-active": _idle = value is true; PublishState(); break;
            case "eof-reached":
                if (value is true) MarkEnded();
                else if (value is false && _loaded) _ended = false;
                PublishState(); break;
            case "time-pos" when value is not null:
                _state = _state with { PositionSeconds = Number(value) }; PublishState(); break;
            case "duration" when value is not null:
                _state = _state with { DurationSeconds = Number(value) }; PublishState(); break;
            case "volume" when value is not null:
                _state = _state with { Volume = (int)Math.Round(Number(value)) }; PublishState(); break;
            case "mute": _state = _state with { IsMuted = value is true }; PublishState(); break;
            case "paused-for-cache": Publish(new BufferingChanged(value is true)); break;
            case "track-list":
                _tracks = ParseTracks(value); Publish(new TracksChanged(_tracks)); break;
            default:
                var next = BuildInfo(_properties);
                if (next != _info) { _info = next; Publish(new MediaInfoChanged(_info)); }
                break;
        }
    }

    private void MarkEnded()
    {
        if (_ended) return;
        _ended = true;
        Trace.WriteLine("[libmpv] 播放结束 EOF。");
        Publish(new EndReached());
    }

    private void PublishState()
    {
        _state = _state with { IsPlaying = _loaded && !_paused && !_idle && !_ended };
        Publish(new PlaybackStateChanged(_state));
    }

    private void Publish(PlayerEvent playerEvent) => _events.Writer.TryWrite(playerEvent);

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
        var hdr = gamma switch { "pq" => "HDR10（当前 SDR 输出）", "hlg" => "HLG（当前 SDR 输出）", null => null, _ => "SDR" };
        var fps = Number(properties.GetValueOrDefault("estimated-vf-fps"));
        if (fps <= 0) fps = Number(properties.GetValueOrDefault("container-fps"));
        var bits = Number(video?.GetValueOrDefault("plane-depth"));
        var cache = properties.GetValueOrDefault("cache-buffering-state");
        return new InfoPanelSnapshot(
            Text(properties.GetValueOrDefault("video-codec")), Text(properties.GetValueOrDefault("audio-codec-name")), hdr,
            width > 0 && height > 0 ? $"{width:0} × {height:0}" : null,
            bits > 0 ? $"{bits:0} bit" : null, fps > 0 ? $"{fps:0.###} fps" : null,
            cache is null ? null : $"缓冲 {Number(cache):0}%");
    }

    private static string? Text(object? value) => value as string;
    private static double Number(object? value) => value switch { double number => number, long integer => integer, int integer => integer, _ => 0 };
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _session.EventReceived -= OnSessionEvent;
            lock (_stateGate) _loadCompletion?.TrySetException(new ObjectDisposedException(nameof(LibMpvBackend)));
            _events.Writer.TryComplete();
        }
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}

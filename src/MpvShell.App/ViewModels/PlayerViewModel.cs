using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using System.Collections.ObjectModel;
using System.Globalization;

namespace MpvShell.App.ViewModels;

public partial class PlayerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPlayerBackend _backend;
    private readonly PlaybackInteractionCoordinator _coordinator;
    private readonly RecentUrlStore _recentUrlStore;
    private bool _isInitialized;
    private CancellationTokenSource? _eventPumpCts;
    private Task? _eventPumpTask;
    private PlaybackState _state = PlaybackState.Initial;
    private string _urlText = string.Empty;
    private Action<Action> _dispatchToUi = action => action();
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly object _disposalGate = new();
    private Task? _disposeTask;
    private bool _disposed;
    private long _seekVersion;
    private long _positionVersion;
    private SeekEstimate? _pendingSeekEstimate;
    private long _mediaVersion;
    private long _transportVersion;
    private long _speedVersion;
    private CancellationTokenSource? _loadCts;
    private bool _isStopping;
    private readonly object _historyGate = new();
    private readonly List<Task> _historyTasks = new();
    private long _historyVersion;
    private string? _recentHistoryMessage;

    // 页面提供 UI 调度器；纯逻辑测试无需创建 WinUI 线程。
    public void SetUiDispatcher(Action<Action> dispatchToUi) =>
        _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));

    public bool IsInitialized => _isInitialized;
    public string PlayPauseLabel => State.IsPlaying ? "暂停" : "播放";
    public string MuteLabel => State.IsMuted ? "取消静音" : "静音";
    public string SpeedLabel => $"倍速 {State.Speed.ToString("0.##", CultureInfo.InvariantCulture)}×";
    public bool IsStopping
    {
        get => _isStopping;
        private set
        {
            if (SetProperty(ref _isStopping, value)) NotifyInteractionProperties();
        }
    }
    public string TimeLabel => $"{FormatTime(State.PositionSeconds)} / {FormatTime(State.DurationSeconds)}";
    public string MediaTitle => string.IsNullOrEmpty(State.CurrentUrl) ? "打开视频开始播放" :
        Uri.TryCreate(State.CurrentUrl, UriKind.Absolute, out var uri) && !uri.IsFile
            ? uri.GetLeftPart(UriPartial.Path)
            : Path.GetFileName(State.CurrentUrl);

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Max(0, seconds) : 0);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    public void ReportError(string message)
    {
        ErrorMessage = message;
        State = State with { AreControlsVisible = true };
    }

    public void RevealControls() => State = State with { AreControlsVisible = true };

    public bool HasRenderingFailure => _renderingErrorMessage is not null;
    public bool HasTerminalFailure => HasRenderingFailure || _terminalFailure is not null;

    public void ReportRenderingFailure(string message)
    {
        // 渲染器要求重启后，音量等成功命令不能清掉终态提示。
        _renderingErrorMessage = message;
        CancelInputInteractions();
        IsBuffering = false;
        State = State with { IsPlaying = false, AreControlsVisible = true };
        OnPropertyChanged(nameof(HasRenderingFailure));
        OnPropertyChanged(nameof(HasTerminalFailure));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(ErrorVisibility));
    }

    private void ReportTerminalFailure(BackendFaulted failure)
    {
        if (HasTerminalFailure) return;
        _terminalFailure = failure;
        CancelInputInteractions();
        IsBuffering = false;
        State = State with { IsPlaying = false, AreControlsVisible = true };
        OnPropertyChanged(nameof(HasTerminalFailure));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(ErrorVisibility));
    }

    public PlayerViewModel(IPlayerBackend backend, PlaybackInteractionCoordinator coordinator)
        : this(backend, coordinator, new GestureDecisionEngine(), new RecentUrlStore(), new InfoPanelViewModel())
    {
    }

    public PlayerViewModel(
        IPlayerBackend backend,
        PlaybackInteractionCoordinator coordinator,
        GestureDecisionEngine gestureDecisionEngine)
        : this(backend, coordinator, gestureDecisionEngine, new RecentUrlStore(), new InfoPanelViewModel())
    {
    }

    public PlayerViewModel(
        IPlayerBackend backend,
        PlaybackInteractionCoordinator coordinator,
        GestureDecisionEngine gestureDecisionEngine,
        RecentUrlStore recentUrlStore,
        InfoPanelViewModel infoPanel)
    {
        _backend = backend;
        _coordinator = coordinator;
        _recentUrlStore = recentUrlStore;
        InfoPanel = infoPanel;
        Tracks.CollectionChanged += (_, _) => RefreshTrackOptions();
        RefreshTrackOptions();
    }

    public InfoPanelViewModel InfoPanel { get; }

    public ObservableCollection<TrackInfo> Tracks { get; } = new();

    public ObservableCollection<string> RecentUrls { get; } = new();

    public Visibility RecentEmptyVisibility => RecentUrls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string? RecentHistoryMessage
    {
        get => _recentHistoryMessage;
        private set
        {
            if (SetProperty(ref _recentHistoryMessage, value)) OnPropertyChanged(nameof(RecentHistoryMessageVisibility));
        }
    }

    public Visibility RecentHistoryMessageVisibility =>
        string.IsNullOrEmpty(RecentHistoryMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ControlsVisibility =>
        State.AreControlsVisible ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InfoPanelVisibility =>
        State.CurrentOverlay == OverlayKind.InfoPanel ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OsdVisibility =>
        State.CurrentOverlay == OverlayKind.Osd ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TracksVisibility =>
        State.CurrentOverlay == OverlayKind.Tracks ? Visibility.Visible : Visibility.Collapsed;

    public PlaybackState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            if (_state.CurrentUrl != value.CurrentUrl || (!_state.IsStopped && value.IsStopped) ||
                (_state.CurrentOverlay != value.CurrentOverlay && value.CurrentOverlay != OverlayKind.None) ||
                (PlaybackInteractionCoordinator.CanSeek(_state) && !PlaybackInteractionCoordinator.CanSeek(value)))
                CancelInputInteractions();
            if (_state.PositionSeconds != value.PositionSeconds)
            {
                _positionVersion++;
                _pendingSeekEstimate = null;
            }
            if (_state.CurrentUrl != value.CurrentUrl || value.IsPlaying || value.IsStopped)
                _hasReachedEnd = false;
            SetProperty(ref _state, value);

            OnPropertyChanged(nameof(ControlsVisibility));
            OnPropertyChanged(nameof(InfoPanelVisibility));
            OnPropertyChanged(nameof(OsdVisibility));
            OnPropertyChanged(nameof(TracksVisibility));
            OnPropertyChanged(nameof(PlayPauseLabel));
            OnPropertyChanged(nameof(MuteLabel));
            OnPropertyChanged(nameof(SpeedLabel));
            OnPropertyChanged(nameof(TimeLabel));
            OnPropertyChanged(nameof(MediaTitle));
            NotifyInteractionProperties();
        }
    }

    public string UrlText
    {
        get => _urlText;
        set => SetProperty(ref _urlText, value);
    }

    private string? _errorMessage;
    private string? _renderingErrorMessage;
    private BackendFaulted? _terminalFailure;

    public string? ErrorMessage
    {
        get => _terminalFailure?.Message ?? _renderingErrorMessage ?? _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ErrorVisibility));
            NotifyPresentationProperties();
        }
    }

    public Visibility ErrorVisibility =>
        string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    private bool _isBuffering;

    public bool IsBuffering
    {
        get => _isBuffering;
        private set
        {
            if (!SetProperty(ref _isBuffering, value)) return;
            OnPropertyChanged(nameof(BufferingVisibility));
            NotifyPresentationProperties();
        }
    }

    public Visibility BufferingVisibility =>
        IsBuffering ? Visibility.Visible : Visibility.Collapsed;

    public async Task InitializeAsync()
    {
        await _initializationGate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (HasTerminalFailure) return;
            if (_isInitialized) return;
            // 历史文件读取与后端初始化独立；慢磁盘或损坏记录不能阻止播放启动。
            _ = StartHistoryOperation(() => _recentUrlStore.InitializeAsync(CancellationToken.None), _historyVersion);
            await _backend.InitializeAsync(CancellationToken.None);
            if (_disposed || HasTerminalFailure) return;
            _isInitialized = true;
            StartEventPump();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (!_disposed) ReportTerminalFailure(new(ex.Message, PlayerErrorKind.Initialization));
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public void OnIdleTimeout()
    {
        if (State.IsPlaying && ErrorMessage is null && !HasActiveInteraction && !HasTimelinePreview)
            State = _coordinator.OnIdleTimeout(State);
    }

    public async Task HandleDragAsync(double deltaX, double deltaY)
    {
        // 旧的一次性入口也使用协调层的浮层优先级与阈值；连续指针输入使用 Begin/Update/Commit。
        if (_disposed || HasTerminalFailure || IsStopping) return;
        var origin = ClampPosition(_pendingSeekEstimate?.Target ?? State.PositionSeconds);
        var request = _coordinator.CreateCompletedGestureRequest(State, deltaX, deltaY, origin);
        if (request?.Kind == PlayerGesture.Volume) await ExecuteVolumeAsync(request.Value, request);
        else if (request?.Kind == PlayerGesture.Seek) await ExecuteSeekAsync(request.Value - origin, true, request);
    }

    public Task SeekToAsync(double seconds) => ExecuteSeekAsync(seconds, isRelative: false);

    [RelayCommand]
    private async Task OpenUrlAsync()
    {
        if (_disposed || HasTerminalFailure || IsStopping) return;
        if (string.IsNullOrWhiteSpace(UrlText))
        {
            return;
        }

        var version = BeginMediaOperation();
        var historyVersion = _historyVersion;
        using var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        ErrorMessage = null;
        _hasReachedEnd = false;
        State = _coordinator.ShowControls(State);
        IsLoading = true;
        try
        {
            await LoadUrlAsync(UrlText.Trim(), version, historyVersion, loadCts.Token);
            if (!IsCurrentMediaOperation(version)) return;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (!IsCurrentMediaOperation(version)) return;
            IsBuffering = false;
            ReportError(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, loadCts))
            {
                _loadCts = null;
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private void ShowControls()
    {
        State = _coordinator.ShowControls(State);
    }

    [RelayCommand]
    private void HideControls()
    {
        State = _coordinator.HideControls(State);
    }

    [RelayCommand]
    private void ToggleOsd()
    {
        State = _coordinator.ToggleOverlay(State, OverlayKind.Osd);
    }

    [RelayCommand]
    private Task ClearRecentHistoryAsync()
    {
        if (_disposed) return Task.CompletedTask;
        // 清空不打断播放，但清空前仍在加载/查询的媒体不能随后重新进入历史。
        var version = ++_historyVersion;
        RecentUrls.Clear();
        OnPropertyChanged(nameof(RecentEmptyVisibility));
        return StartHistoryOperation(() => _recentUrlStore.ClearAsync(CancellationToken.None), version);
    }

    [RelayCommand]
    private void ToggleTracks()
    {
        State = _coordinator.ToggleOverlay(State, OverlayKind.Tracks);
    }

    [RelayCommand]
    private void ToggleInfoPanel()
    {
        State = _coordinator.ToggleOverlay(State, OverlayKind.InfoPanel);
    }

    public Task ChangeVolumeAsync(double volume) => ExecuteVolumeAsync(volume);

    private async Task ExecuteVolumeAsync(double volume, InteractionRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (_disposed || _terminalFailure is not null || !double.IsFinite(volume)) return;
        request ??= _coordinator.CreateRequest(PlayerGesture.Volume, Math.Clamp(volume, 0, 100));
        var version = ++_volumeVersion;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_inputCts.Token, cancellationToken);
        if (!HasTerminalFailure) _coordinator.MarkSubmitted(request);
        NotifyInteractionProperties();
        try
        {
            var value = (int)Math.Clamp(volume, 0, 100);
            await _backend.SetVolumeAsync(value, linked.Token);
            if (_disposed || _terminalFailure is not null || version != _volumeVersion || !_coordinator.IsCurrent(request)) return;
            linked.Token.ThrowIfCancellationRequested();
            State = State with { Volume = value };
            ErrorMessage = null;
            if (!HasTerminalFailure) _coordinator.Complete(request, $"音量 {value}%");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            _coordinator.Complete(request, string.Empty);
        }
        catch (Exception ex)
        {
            if (_disposed || _terminalFailure is not null || version != _volumeVersion || !_coordinator.IsCurrent(request)) return;
            ReportError(ex.Message);
            _coordinator.Complete(request, "音量调整失败");
        }
        finally { NotifyInteractionProperties(); }
    }

    [RelayCommand]
    private async Task ToggleMuteAsync()
    {
        if (_disposed || _terminalFailure is not null) return;
        try
        {
            var muted = !State.IsMuted;
            await _backend.SetMuteAsync(muted, CancellationToken.None);
            if (_disposed || _terminalFailure is not null) return;
            State = State with { IsMuted = muted };
            ErrorMessage = null;
        }
        catch (Exception ex) { ReportError(ex.Message); }
    }

    [RelayCommand]
    private async Task TogglePlayPauseAsync()
    {
        if (_disposed || HasTerminalFailure || IsStopping || IsLoading) return;
        if (string.IsNullOrWhiteSpace(State.CurrentUrl))
        {
            ReportError("请先打开媒体。");
            return;
        }
        if (!State.IsPlaying && (State.IsStopped || _hasReachedEnd))
        {
            await ReplayAsync();
            return;
        }
        var version = _transportVersion;
        try
        {
            if (State.IsPlaying)
            {
                await _backend.PauseAsync(CancellationToken.None);
                if (!IsCurrentTransportOperation(version)) return;
                State = _coordinator.ShowControls(State with { IsPlaying = false });
                ErrorMessage = null;
                return;
            }

            await _backend.PlayAsync(CancellationToken.None);
            // 等待后端命令时可能发生不可恢复故障，不能用迟到的回复覆盖终态。
            if (!IsCurrentTransportOperation(version)) return;
            State = _coordinator.ShowControls(State.IsStopped ? State : State with { IsPlaying = true });
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (IsCurrentTransportOperation(version)) ReportError(ex.Message);
        }
    }

    private async Task ReplayAsync()
    {
        // 后端的 stopped/EOF 播放会重新打开媒体，须与显式打开一样可等待、可停止。
        var version = BeginMediaOperation();
        using var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        ErrorMessage = null;
        _hasReachedEnd = false;
        IsBuffering = false;
        State = _coordinator.ShowControls(State);
        IsLoading = true;
        try
        {
            await _backend.PlayAsync(loadCts.Token);
            if (!IsCurrentMediaOperation(version)) return;
            // 重播成功后仍由后端事件提供播放/停止、轨道与媒体信息，不虚构新媒体状态。
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (!IsCurrentMediaOperation(version)) return;
            IsBuffering = false;
            ReportError(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, loadCts))
            {
                _loadCts = null;
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (_disposed || HasTerminalFailure || IsStopping) return;
        var version = BeginMediaOperation();
        IsStopping = true;
        RevealControls();
        try
        {
            await _backend.StopAsync(CancellationToken.None);
            if (!IsCurrentMediaOperation(version)) return;
            // 是否停止及清空后的媒体数据由后端事件确认，命令回复不虚构播放状态。
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (IsCurrentMediaOperation(version)) ReportError(ex.Message);
        }
        finally { IsStopping = false; }
    }

    public async Task ChangeSpeedAsync(double speed)
    {
        if (_disposed || HasTerminalFailure) return;
        var version = ++_speedVersion;
        var mediaVersion = _mediaVersion;
        if (!double.IsFinite(speed) || speed is < 0.25 or > 4)
        {
            ReportError("播放倍速须在 0.25× 至 4× 之间。");
            return;
        }
        try
        {
            await _backend.SetSpeedAsync(speed, CancellationToken.None);
            if (!IsCurrentMediaOperation(mediaVersion) || version != _speedVersion) return;
            // 只显示原生 speed 属性回传的值，失败或迟到的命令不会回填选择值。
            RevealControls();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (IsCurrentMediaOperation(mediaVersion) && version == _speedVersion) ReportError(ex.Message);
        }
    }

    [RelayCommand]
    private Task SeekBackwardAsync() => SeekRelativeAsync(-15);

    [RelayCommand]
    private Task SeekForwardAsync() => SeekRelativeAsync(30);

    [RelayCommand]
    private async Task SelectTrackAsync(TrackInfo? track)
    {
        if (_disposed || HasTerminalFailure || IsStopping || IsLoading || State.IsStopped || track is null ||
            (!IsAudioTrack(track) && !IsSubtitleTrack(track)))
        {
            return;
        }

        var version = _transportVersion;
        try
        {
            if (IsAudioTrack(track))
            {
                await _backend.SetAudioTrackAsync(track.Id, CancellationToken.None);
            }
            else
            {
                await _backend.SetSubtitleTrackAsync(track.Id, CancellationToken.None);
            }

            if (!IsCurrentTransportOperation(version)) return;
            // 命令完成仅代表请求已受理；选中标记等待原生 track-list 事件。
            RevealControls();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (IsCurrentTransportOperation(version)) ReportError(ex.Message);
        }
    }

    private Task SeekRelativeAsync(double deltaSeconds) => ExecuteSeekAsync(deltaSeconds, isRelative: true);

    private async Task ExecuteSeekAsync(double seconds, bool isRelative, InteractionRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (!CanSeek || !double.IsFinite(seconds)) return;
        // 命令回复仅确认请求；没有实际位置事件时才使用预计落点。
        // 连续相对跳转沿用待确认目标，绝对与相对请求共享同一个回复顺序。
        var origin = ClampPosition(_pendingSeekEstimate?.Target ?? State.PositionSeconds);
        var target = ClampPosition(isRelative ? origin + seconds : seconds);
        request ??= _coordinator.CreateRequest(PlayerGesture.Seek, target);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_inputCts.Token, cancellationToken);
        _coordinator.MarkSubmitted(request);
        NotifyInteractionProperties();
        var estimate = isRelative && _pendingSeekEstimate is not null ? _pendingSeekEstimate : new SeekEstimate();
        estimate.Target = target;
        var seekVersion = ++_seekVersion;
        estimate.LatestVersion = seekVersion;
        estimate.PositionVersion = _positionVersion;
        estimate.PendingCount++;
        _pendingSeekEstimate = estimate;
        try
        {
            if (isRelative) await _backend.SeekAsync(target - origin, linked.Token);
            else await _backend.SetPositionAsync(target, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            // 过时错误不显示，但仍须作废依赖本请求的预计链；后来的绝对目标有独立链。
            estimate.PendingCount--;
            estimate.IsValid = false;
            if (!IsCurrentSeek(seekVersion)) return;
            _pendingSeekEstimate = estimate.PositionVersion == _positionVersion ? estimate : null;
            if (ex is OperationCanceledException && linked.IsCancellationRequested)
            {
                _coordinator.Complete(request, string.Empty);
                NotifyInteractionProperties();
                return;
            }
            ErrorMessage = ex.Message;
            _coordinator.Complete(request, "跳转失败");
            NotifyInteractionProperties();
            return;
        }

        estimate.PendingCount--;
        if (IsCurrentSeek(seekVersion))
        {
            State = _coordinator.ShowControls(State);
            ErrorMessage = null;
            _coordinator.Complete(request, $"跳转至 {FormatTime(target)}");
            NotifyInteractionProperties();
        }
        // 回复可乱序；整条依赖链成功后才能应用最新目标，不能提前假定前置请求成功。
        if (estimate.PendingCount != 0 || !IsCurrentSeek(estimate.LatestVersion)) return;
        var positionUnchanged = estimate.PositionVersion == _positionVersion;
        // 超时不保证撤销命令，无效相对预计链需等待实际位置或独立的绝对目标。
        _pendingSeekEstimate = !estimate.IsValid && positionUnchanged ? estimate : null;
        if (estimate.IsValid && positionUnchanged)
            State = _coordinator.ShowControls(State with { PositionSeconds = estimate.Target });
    }

    private bool IsCurrentSeek(long version) => !_disposed && !HasTerminalFailure && version == _seekVersion;

    private void InvalidateSeek()
    {
        _seekVersion++;
        _pendingSeekEstimate = null;
    }

    private sealed class SeekEstimate
    {
        internal double Target { get; set; }
        internal bool IsValid { get; set; } = true;
        internal int PendingCount { get; set; }
        internal long LatestVersion { get; set; }
        internal long PositionVersion { get; set; }
    }

    private long BeginMediaOperation()
    {
        // 包含同地址重新打开；旧请求的成功或失败均不能污染新的加载或停止。
        var version = ++_mediaVersion;
        _transportVersion++;
        CancelInputInteractions();
        _loadCts?.Cancel();
        _loadCts = null;
        IsLoading = false;
        return version;
    }

    private bool IsCurrentMediaOperation(long version) =>
        !_disposed && !HasTerminalFailure && version == _mediaVersion;

    private bool IsCurrentTransportOperation(long version) =>
        !_disposed && !HasTerminalFailure && version == _transportVersion;

    private async Task LoadUrlAsync(string url, long version, long historyVersion, CancellationToken cancellationToken)
    {
        // 切换媒体时清除旧文件的缓冲状态；新文件的状态仍由后端事件决定。
        IsBuffering = false;
        await _backend.LoadUrlAsync(url, cancellationToken);
        if (!IsCurrentMediaOperation(version)) return;

        var info = await _backend.GetInfoSnapshotAsync(cancellationToken);
        if (!IsCurrentMediaOperation(version)) return;
        var tracks = await _backend.GetTracksAsync(cancellationToken);
        if (!IsCurrentMediaOperation(version)) return;

        InfoPanel.Update(info);
        ReplaceTracks(tracks);

        UrlText = url;
        State = _coordinator.ShowControls(State with { CurrentUrl = url });

        // 保存属于已成功打开的媒体；不使用加载取消令牌，也不等待磁盘后回填旧媒体状态。
        if (historyVersion == _historyVersion)
            _ = StartHistoryOperation(() => _recentUrlStore.AddAsync(url, CancellationToken.None), historyVersion);
    }

    private Task StartHistoryOperation(Func<Task> operation, long version)
    {
        lock (_historyGate)
        {
            if (_disposed) return Task.CompletedTask;
            var task = ObserveHistoryOperationAsync(operation, version);
            _historyTasks.RemoveAll(pending => pending.IsCompleted);
            _historyTasks.Add(task);
            return task;
        }
    }

    private async Task ObserveHistoryOperationAsync(Func<Task> operation, long version)
    {
        string? failure = null;
        try { await operation(); }
        catch (Exception ex)
        {
            // 存储故障单独显示；不泄漏地址、文件内容或令牌，也不变成播放失败。
            failure = "最近打开记录暂时无法更新；本次播放不受影响。";
            System.Diagnostics.Trace.WriteLine($"[history] 历史操作未完成：{ex.GetType().Name}");
        }
        if (_disposed || version != _historyVersion) return;
        RefreshRecentUrls();
        RecentHistoryMessage = failure ?? _recentUrlStore.PersistenceError;
    }

    internal Task WaitForRecentHistoryAsync()
    {
        lock (_historyGate) return Task.WhenAll(_historyTasks.ToArray());
    }

    private void RefreshRecentUrls()
    {
        RecentUrls.Clear();

        foreach (var recentUrl in _recentUrlStore.Items)
        {
            RecentUrls.Add(recentUrl);
        }
        OnPropertyChanged(nameof(RecentEmptyVisibility));
    }

    private void ReplaceTracks(IReadOnlyList<TrackInfo> tracks)
    {
        _replacingTracks = true;
        try
        {
            Tracks.Clear();
            foreach (var track in tracks) Tracks.Add(track);
        }
        finally
        {
            _replacingTracks = false;
            RefreshTrackOptions();
        }
    }

    private static bool IsAudioTrack(TrackInfo track) =>
        string.Equals(track.Kind, "audio", StringComparison.OrdinalIgnoreCase);

    private double ClampPosition(double seconds) =>
        PlaybackInteractionCoordinator.ClampPosition(seconds, State.DurationSeconds);

    private void StartEventPump()
    {
        _eventPumpCts?.Cancel();
        _eventPumpCts?.Dispose();

        _eventPumpCts = new CancellationTokenSource();
        var token = _eventPumpCts.Token;
        _eventPumpTask = Task.Run(() => ObserveBackendEventsAsync(token));
    }

    private async Task ObserveBackendEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await UiPlayerEventPump.RunAsync(_backend.ObserveEventsAsync(cancellationToken),
                action => _dispatchToUi(action), ApplyBackendEvent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            try
            {
                _dispatchToUi(() =>
                {
                    if (_disposed || cancellationToken.IsCancellationRequested) return;
                    ReportTerminalFailure(new(ex.Message, PlayerErrorKind.EventStream));
                });
            }
            catch (Exception dispatchError)
            {
                System.Diagnostics.Trace.WriteLine($"UI 故障通知未完成：{dispatchError.Message}；原始错误：{ex}");
            }
            // UI 观察失败时 core 可能仍正常播放；禁用控制前后的清理不能只改变界面。
            // 原始故障仍是终态原因，暂停失败不能覆盖它或阻碍后续释放。
            try { await _backend.PauseAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception pauseError) { System.Diagnostics.Trace.WriteLine($"事件观察失败后暂停未完成：{pauseError.Message}"); }
        }
    }

    private void ApplyBackendEvent(PlayerEvent playerEvent)
    {
        if (_disposed || HasTerminalFailure) return;
        switch (playerEvent)
        {
            case PlaybackStateChanged stateChanged:
                if (!State.IsStopped && stateChanged.State.IsStopped)
                {
                    // 排队的旧停止事件只能废弃播放/跳转回复，不能取消用户已提交的新加载。
                    _transportVersion++;
                    InvalidateSeek();
                    IsBuffering = false;
                    Tracks.Clear();
                    InfoPanel.Update(new(null, null, null, null, null, null, null));
                }
                State = stateChanged.State with
                {
                    AreControlsVisible = State.AreControlsVisible || !stateChanged.State.IsPlaying,
                    CurrentOverlay = State.CurrentOverlay,
                };
                break;
            case TracksChanged tracksChanged:
                ReplaceTracks(tracksChanged.Tracks);
                break;
            case MediaInfoChanged infoChanged:
                InfoPanel.Update(infoChanged.Snapshot);
                break;
            case BufferingChanged bufferingChanged:
                IsBuffering = bufferingChanged.IsBuffering;
                break;
            case EndReached:
                _transportVersion++;
                CancelInputInteractions();
                IsBuffering = false;
                State = State with { IsPlaying = false, AreControlsVisible = true };
                // 已排队的旧结束事件不能覆盖本次打开的加载提示或留下结束标记。
                if (!IsLoading && !State.IsStopped) _hasReachedEnd = true;
                NotifyPresentationProperties();
                break;
            case BackendFaulted faulted:
                CancelInputInteractions();
                IsBuffering = false;
                if (faulted.IsTerminal) ReportTerminalFailure(faulted);
                else ReportError(faulted.Message);
                break;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposalGate) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        lock (_historyGate) _disposed = true;
        CancelInputInteractions();
        _loadCts?.Cancel();
        await _initializationGate.WaitAsync();
        try
        {
            _eventPumpCts?.Cancel();
            if (_eventPumpTask is not null) await _eventPumpTask;
            _eventPumpCts?.Dispose();
            await _backend.DisposeAsync();
        }
        finally
        {
            _initializationGate.Release();
            // 先释放播放资源，再等已接受的写入完成；窗口关闭后不再更新历史界面。
            await WaitForRecentHistoryAsync();
        }
    }
}

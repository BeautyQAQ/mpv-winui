using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using System.Collections.ObjectModel;

namespace MpvShell.App.ViewModels;

public partial class PlayerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPlayerBackend _backend;
    private readonly PlaybackInteractionCoordinator _coordinator;
    private readonly GestureDecisionEngine _gestureDecisionEngine;
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

    // 页面提供 UI 调度器；纯逻辑测试无需创建 WinUI 线程。
    public void SetUiDispatcher(Action<Action> dispatchToUi) =>
        _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));

    public bool IsInitialized => _isInitialized;
    public string PlayPauseLabel => State.IsPlaying ? "暂停" : "播放";
    public string MuteLabel => State.IsMuted ? "取消静音" : "静音";
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
        _gestureDecisionEngine = gestureDecisionEngine;
        _recentUrlStore = recentUrlStore;
        InfoPanel = infoPanel;
    }

    public InfoPanelViewModel InfoPanel { get; }

    public ObservableCollection<TrackInfo> Tracks { get; } = new();

    public ObservableCollection<string> RecentUrls { get; } = new();

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

            if (_state.CurrentUrl != value.CurrentUrl) InvalidateSeek();
            if (_state.PositionSeconds != value.PositionSeconds)
            {
                _positionVersion++;
                _pendingSeekEstimate = null;
            }
            SetProperty(ref _state, value);

            OnPropertyChanged(nameof(ControlsVisibility));
            OnPropertyChanged(nameof(InfoPanelVisibility));
            OnPropertyChanged(nameof(OsdVisibility));
            OnPropertyChanged(nameof(TracksVisibility));
            OnPropertyChanged(nameof(PlayPauseLabel));
            OnPropertyChanged(nameof(MuteLabel));
            OnPropertyChanged(nameof(TimeLabel));
            OnPropertyChanged(nameof(MediaTitle));
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
        if (State.IsPlaying && ErrorMessage is null)
            State = _coordinator.OnIdleTimeout(State);
    }

    public async Task HandleDragAsync(double deltaX, double deltaY)
    {
        var gesture = _gestureDecisionEngine.Classify(deltaX, deltaY);
        if (gesture == PlayerGesture.Volume)
        {
            await ChangeVolumeAsync(State.Volume - deltaY / 4);
            return;
        }
        if (gesture != PlayerGesture.Seek)
        {
            return;
        }

        var deltaSeconds = deltaX / 8.0;

        if (Math.Abs(deltaSeconds) < 1)
        {
            return;
        }

        await SeekRelativeAsync(deltaSeconds);
    }

    public Task SeekToAsync(double seconds) => ExecuteSeekAsync(seconds, isRelative: false);

    [RelayCommand]
    private async Task OpenUrlAsync()
    {
        if (_disposed || HasTerminalFailure) return;
        if (string.IsNullOrWhiteSpace(UrlText))
        {
            return;
        }

        try
        {
            await LoadUrlAsync(UrlText.Trim());
            if (_disposed || HasTerminalFailure) return;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            IsBuffering = false;
            ErrorMessage = ex.Message;
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
    private void ToggleTracks()
    {
        State = _coordinator.ToggleOverlay(State, OverlayKind.Tracks);
    }

    [RelayCommand]
    private void ToggleInfoPanel()
    {
        State = _coordinator.ToggleOverlay(State, OverlayKind.InfoPanel);
    }

    public async Task ChangeVolumeAsync(double volume)
    {
        if (_disposed || _terminalFailure is not null) return;
        try
        {
            var value = (int)Math.Clamp(volume, 0, 100);
            await _backend.SetVolumeAsync(value, CancellationToken.None);
            if (_disposed || _terminalFailure is not null) return;
            State = State with { Volume = value };
            ErrorMessage = null;
        }
        catch (Exception ex) { ReportError(ex.Message); }
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
        if (_disposed || HasTerminalFailure) return;
        if (string.IsNullOrWhiteSpace(State.CurrentUrl))
        {
            ReportError("请先打开媒体。");
            return;
        }
        try
        {
            if (State.IsPlaying)
            {
                await _backend.PauseAsync(CancellationToken.None);
                if (_disposed || HasTerminalFailure) return;
                State = _coordinator.ShowControls(State with { IsPlaying = false });
                ErrorMessage = null;
                return;
            }

            await _backend.PlayAsync(CancellationToken.None);
            // 等待后端命令时可能发生不可恢复故障，不能用迟到的回复覆盖终态。
            if (_disposed || HasTerminalFailure) return;
            State = _coordinator.ShowControls(State with { IsPlaying = true });
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private Task SeekBackwardAsync() => SeekRelativeAsync(-15);

    [RelayCommand]
    private Task SeekForwardAsync() => SeekRelativeAsync(30);

    [RelayCommand]
    private async Task SelectTrackAsync(TrackInfo? track)
    {
        if (_disposed || HasTerminalFailure || track is null)
        {
            return;
        }

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

            if (_disposed || HasTerminalFailure) return;
            ReplaceTrackSelection(track);
            State = _coordinator.ShowControls(State);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private Task SeekRelativeAsync(double deltaSeconds) => ExecuteSeekAsync(deltaSeconds, isRelative: true);

    private async Task ExecuteSeekAsync(double seconds, bool isRelative)
    {
        if (_disposed || HasTerminalFailure) return;
        // 命令回复仅确认请求；没有实际位置事件时才使用预计落点。
        // 连续相对跳转沿用待确认目标，绝对与相对请求共享同一个回复顺序。
        var target = ClampPosition(isRelative ? (_pendingSeekEstimate?.Target ?? State.PositionSeconds) + seconds : seconds);
        var estimate = isRelative && _pendingSeekEstimate is not null ? _pendingSeekEstimate : new SeekEstimate();
        estimate.Target = target;
        var seekVersion = ++_seekVersion;
        estimate.LatestVersion = seekVersion;
        estimate.PositionVersion = _positionVersion;
        estimate.PendingCount++;
        _pendingSeekEstimate = estimate;
        try
        {
            if (isRelative) await _backend.SeekAsync(seconds, CancellationToken.None);
            else await _backend.SetPositionAsync(target, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 过时错误不显示，但仍须作废依赖本请求的预计链；后来的绝对目标有独立链。
            estimate.PendingCount--;
            estimate.IsValid = false;
            if (!IsCurrentSeek(seekVersion)) return;
            _pendingSeekEstimate = estimate.PositionVersion == _positionVersion ? estimate : null;
            ErrorMessage = ex.Message;
            return;
        }

        estimate.PendingCount--;
        if (IsCurrentSeek(seekVersion))
        {
            State = _coordinator.ShowControls(State);
            ErrorMessage = null;
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

    private async Task LoadUrlAsync(string url)
    {
        // 包含同地址重新打开；旧请求的成功或失败均不能污染新的加载。
        InvalidateSeek();
        // 切换媒体时清除旧文件的缓冲状态；新文件的状态仍由后端事件决定。
        IsBuffering = false;
        await _backend.LoadUrlAsync(url, CancellationToken.None);
        if (_disposed || HasTerminalFailure) return;

        var info = await _backend.GetInfoSnapshotAsync(CancellationToken.None);
        if (_disposed || HasTerminalFailure) return;
        var tracks = await _backend.GetTracksAsync(CancellationToken.None);
        if (_disposed || HasTerminalFailure) return;

        _recentUrlStore.Add(url);
        RefreshRecentUrls();

        InfoPanel.Update(info);
        ReplaceTracks(tracks);

        UrlText = url;
        State = _coordinator.ShowControls(State with { CurrentUrl = url });
    }

    private void RefreshRecentUrls()
    {
        RecentUrls.Clear();

        foreach (var recentUrl in _recentUrlStore.Items)
        {
            RecentUrls.Add(recentUrl);
        }
    }

    private void ReplaceTracks(IReadOnlyList<TrackInfo> tracks)
    {
        Tracks.Clear();

        foreach (var track in tracks)
        {
            Tracks.Add(track);
        }
    }

    private void ReplaceTrackSelection(TrackInfo selectedTrack)
    {
        for (var i = 0; i < Tracks.Count; i++)
        {
            var track = Tracks[i];
            var sameFamily = string.Equals(track.Kind, selectedTrack.Kind, StringComparison.OrdinalIgnoreCase);
            Tracks[i] = sameFamily ? track with { Selected = track.Id == selectedTrack.Id } : track;
        }
    }

    private static bool IsAudioTrack(TrackInfo track) =>
        string.Equals(track.Kind, "audio", StringComparison.OrdinalIgnoreCase);

    private double ClampPosition(double seconds) =>
        State.DurationSeconds > 0
            ? Math.Clamp(seconds, 0, State.DurationSeconds)
            : Math.Max(0, seconds);

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
                InvalidateSeek();
                IsBuffering = false;
                State = State with { IsPlaying = false, AreControlsVisible = true };
                break;
            case BackendFaulted faulted:
                InvalidateSeek();
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
        _disposed = true;
        await _initializationGate.WaitAsync();
        try
        {
            _eventPumpCts?.Cancel();
            if (_eventPumpTask is not null) await _eventPumpTask;
            _eventPumpCts?.Dispose();
            await _backend.DisposeAsync();
        }
        finally { _initializationGate.Release(); }
    }
}

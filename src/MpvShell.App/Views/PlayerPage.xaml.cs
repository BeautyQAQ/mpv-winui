using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Models;
using MpvShell.Player.LibMpv;
using MpvShell.Rendering.WinUI;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace MpvShell.App.Views;

public sealed partial class PlayerPage : Page
{
    private DispatcherQueueTimer? _autoHideTimer;
    private DispatcherQueueTimer? _seekTimer;
    private DispatcherQueueTimer? _volumeTimer;
    private Point? _dragStartPoint;
    private bool _initializeRequested;
    private bool _ready;
    private bool _updatingSliders;
    private bool _scrubbing;
    private bool _gestureBusy;
    private Task? _initializationTask;
    private Task? _releaseTask;
    private DisplayOutputMonitor? _displayOutputMonitor;
    private bool _outputRefreshRequested;
    private bool _outputRefreshRunning;
    private Task _outputRefreshTask = Task.CompletedTask;
    private readonly D3D11VideoSurfaceRenderer _videoSurfaceRenderer = new();
    // 启动参数只在首个页面实例消费；同进程内页面重新挂载不得重放启动加载。
    private static int _startupMediaClaimed;
#if DEBUG
    /// <summary>恢复验收探针使用：页面重新挂载后取得新页面的渲染器。</summary>
    internal D3D11VideoSurfaceRenderer DebugRenderer => _videoSurfaceRenderer;
#endif
    public PlayerViewModel ViewModel { get; }

    public PlayerPage()
    {
        InitializeComponent();
        ViewModel = ((App)Application.Current).Services.GetRequiredService<PlayerViewModel>();
        ViewModel.SetUiDispatcher(action =>
        {
            if (DispatcherQueue.HasThreadAccess) action();
            else DispatcherQueue.TryEnqueue(() => action());
        });
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = ViewModel;
        ViewModel.RevealControls();
        Timeline.AddHandler(PointerPressedEvent, new PointerEventHandler(OnTimelinePressed), true);
        Timeline.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnTimelineReleased), true);
        Timeline.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnTimelineReleased), true);
        _videoSurfaceRenderer.RenderingFailed += OnRenderingFailed;
        _videoSurfaceRenderer.OutputStatusChanged += OnOutputStatusChanged;
        ViewModel.InfoPanel.PropertyChanged += OnInfoPanelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initializeRequested) return;
        _initializeRequested = true;
        _initializationTask = InitializePlayerAsync();
        await _initializationTask;
#if DEBUG
        await Diagnostics.AppRecoveryProbe.RunIfRequestedAsync(this, ViewModel, _videoSurfaceRenderer,
            ((App)Application.Current).Services.GetRequiredService<IMpvPlayerSession>(),
            ((MainWindow)((App)Application.Current).MainWindowInstance!).ShutdownPlayerAsync, _ready,
            () => OpenMediaPanel.IsEnabled || TransportControls.IsEnabled);
#endif
    }

    private async Task InitializePlayerAsync()
    {
        try
        {
            await ViewModel.InitializeAsync();
            if (!ViewModel.IsInitialized || ViewModel.HasTerminalFailure) return;
            var session = ((App)Application.Current).Services.GetRequiredService<MpvPlayerSession>();
            await _videoSurfaceRenderer.InitializeAsync(session, CancellationToken.None);
            if (_releaseTask is not null || ViewModel.HasTerminalFailure) return;
            await _videoSurfaceRenderer.AttachAsync(VideoSurface, CancellationToken.None);
            if (_releaseTask is not null || ViewModel.HasTerminalFailure) return;
            // 页面重新挂载或同进程内重建页面后恢复会话挂起的视频；首次启动时为 no-op。
            await session.ResumeVideoAfterDetachAsync(CancellationToken.None);
            _ready = true;
            OpenMediaPanel.IsEnabled = TransportControls.IsEnabled = true;
            XamlRoot.Changed += OnXamlRootChanged;
            var window = ((App)Application.Current).MainWindowInstance!;
            _displayOutputMonitor = new DisplayOutputMonitor(WinRT.Interop.WindowNative.GetWindowHandle(window));
            _displayOutputMonitor.Changed += OnDisplayOutputChanged;
            window.AppWindow.Changed += OnAppWindowChanged;
            QueueOutputRefresh();
            await _outputRefreshTask;
            CreateTimers();
            SyncSliders();
            var mediaArgument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
            // 只有首个页面实例消费启动媒体参数；后续重新挂载保持当前媒体，不重新打开。
            if (!string.IsNullOrWhiteSpace(mediaArgument) && Interlocked.Exchange(ref _startupMediaClaimed, 1) == 0)
                await OpenMediaAsync(mediaArgument);
#if DEBUG
            Diagnostics.AppRecoveryProbe.NotifyPageReady(this);
#endif
        }
        catch (Exception ex) { ViewModel.ReportRenderingFailure($"播放器初始化失败：{ex.Message}"); }
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e) => await ReleaseAsync();

    /// <summary>释放页面自身资源（渲染器、计时器、事件订阅）；共享播放会话由应用关闭流程销毁。</summary>
    public Task ReleaseAsync() => _releaseTask ??= ReleaseCoreAsync();

    private async Task ReleaseCoreAsync()
    {
        _ready = false;
        _autoHideTimer?.Stop();
        _seekTimer?.Stop();
        _volumeTimer?.Stop();
        if (_initializationTask is not null) await _initializationTask;
        if (XamlRoot is not null) XamlRoot.Changed -= OnXamlRootChanged;
        if (((App)Application.Current).MainWindowInstance is { } window)
            window.AppWindow.Changed -= OnAppWindowChanged;
        ViewModel.InfoPanel.PropertyChanged -= OnInfoPanelPropertyChanged;
        if (_displayOutputMonitor is not null)
        {
            _displayOutputMonitor.Changed -= OnDisplayOutputChanged;
            _displayOutputMonitor.Dispose();
            _displayOutputMonitor = null;
        }
        await _outputRefreshTask;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _videoSurfaceRenderer.RenderingFailed -= OnRenderingFailed;
        _videoSurfaceRenderer.OutputStatusChanged -= OnOutputStatusChanged;
        // 释放渲染器会先挂起会话视频（若有活动媒体）并归还 render context，供重新挂载恢复；
        // 这里不销毁共享会话，窗口关闭与应用退出由 MainWindow.ShutdownPlayerAsync 统一收口。
        await _videoSurfaceRenderer.DisposeAsync();
    }

    private void OnRenderingFailed(string message) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_releaseTask is not null) return;
        _ready = false;
        _autoHideTimer?.Stop();
        _seekTimer?.Stop();
        _volumeTimer?.Stop();
        OpenMediaPanel.IsEnabled = TransportControls.IsEnabled = false;
        ViewModel.ReportRenderingFailure(message);
    });
    private void OnOutputStatusChanged(string message) => DispatcherQueue.TryEnqueue(() => ViewModel.InfoPanel.SetOutputSummary(message));

    private void OnDisplayOutputChanged(DisplayOutputCapabilities capabilities) => QueueOutputRefresh();

    private void OnInfoPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InfoPanelViewModel.IsHdrSource)) QueueOutputRefresh();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_ready && (args.DidPositionChange || args.DidSizeChange)) _displayOutputMonitor?.Refresh();
    }

    private void QueueOutputRefresh()
    {
        if (!_ready || _releaseTask is not null) return;
        _outputRefreshRequested = true;
        if (_outputRefreshRunning) return;
        _outputRefreshRunning = true;
        _outputRefreshTask = RefreshOutputLoopAsync();
    }

    private async Task RefreshOutputLoopAsync()
    {
        try
        {
            while (_outputRefreshRequested && _ready && _releaseTask is null)
            {
                _outputRefreshRequested = false;
                try
                {
                    if (_displayOutputMonitor is not null)
                        await _videoSurfaceRenderer.UpdateOutputAsync(ViewModel.InfoPanel.IsHdrSource,
                            _displayOutputMonitor.Current, CancellationToken.None);
                }
                catch (Exception ex) { ViewModel.ReportError($"切换视频色彩输出失败：{ex.Message}"); }
            }
        }
        finally { _outputRefreshRunning = false; }
    }

    private async void OnVideoSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => await ResizeVideoAsync();
    private async void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_ready) _displayOutputMonitor?.Refresh();
        await ResizeVideoAsync();
    }

    private async Task ResizeVideoAsync()
    {
        if (!_ready || VideoSurface.ActualWidth <= 0 || VideoSurface.ActualHeight <= 0) return;
        try
        {
            // UIElement.RasterizationScale 是元素级额外缩放，默认恒为 1.0；显示器 DPI 缩放只有 XamlRoot 才知道。
            await _videoSurfaceRenderer.ResizeAsync(new VideoSurfaceSize(
                VideoSurface.ActualWidth, VideoSurface.ActualHeight, VideoSurface.XamlRoot?.RasterizationScale ?? 1.0), CancellationToken.None);
        }
        catch (Exception ex) { ViewModel.ReportError($"调整视频尺寸失败：{ex.Message}"); }
    }

    private async Task OpenMediaAsync(string source)
    {
        if (!_ready) return;
        ViewModel.UrlText = source;
        await ViewModel.OpenUrlCommand.ExecuteAsync(null);
        RestartAutoHideTimer();
    }

    private async void OnOpenFileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _autoHideTimer?.Stop();
            var window = ((App)Application.Current).MainWindowInstance!;
            var picker = new FileOpenPicker(window.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
                CommitButtonText = "播放",
                FileTypeFilter = { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".ts", ".m4v", ".mp3", ".flac", ".wav" },
            };
            var file = await picker.PickSingleFileAsync();
            if (file is not null) await OpenMediaAsync(file.Path);
        }
        catch (Exception ex) { ViewModel.ReportError($"打开文件失败：{ex.Message}"); }
        finally { RestartAutoHideTimer(); }
    }

    private async void OnMediaInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await OpenMediaAsync(MediaInput.Text);
    }

    private async void OnRecentMediaClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string source) await OpenMediaAsync(source);
    }

    private async void OnTrackClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackInfo track) await ViewModel.SelectTrackCommand.ExecuteAsync(track);
    }

    private void OnAnyPointerActivity(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.RevealControls();
        RestartAutoHideTimer();
    }

    private void OnVideoPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragStartPoint = e.GetCurrentPoint(InteractionSurface).Position;
        InteractionSurface.CapturePointer(e.Pointer);
        Focus(FocusState.Programmatic);
        ViewModel.RevealControls();
    }

    private async void OnVideoPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_ready || _gestureBusy || _dragStartPoint is null || !e.GetCurrentPoint(InteractionSurface).IsInContact) return;
        var current = e.GetCurrentPoint(InteractionSurface).Position;
        var deltaX = current.X - _dragStartPoint.Value.X;
        var deltaY = current.Y - _dragStartPoint.Value.Y;
        if (Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)) <= 40) return;
        _dragStartPoint = current;
        _gestureBusy = true;
        try { await ViewModel.HandleDragAsync(deltaX, deltaY); }
        finally { _gestureBusy = false; }
    }

    private void OnVideoPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragStartPoint = null;
        InteractionSurface.ReleasePointerCaptures();
        RestartAutoHideTimer();
    }

    private void OnVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ToggleFullscreen();
    private void OnFullscreenClicked(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        var window = ((App)Application.Current).MainWindowInstance!;
        var isFullscreen = window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        window.AppWindow.SetPresenter(isFullscreen ? AppWindowPresenterKind.Default : AppWindowPresenterKind.FullScreen);
        FullscreenButton.Content = isFullscreen ? "全屏" : "退出全屏";
        ViewModel.RevealControls();
    }

    private async void OnPlayerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_releaseTask is not null) return;
        var focusedControl = FocusManager.GetFocusedElement(XamlRoot);
        if (e.Key == VirtualKey.F11) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == VirtualKey.Escape)
        {
            if (((App)Application.Current).MainWindowInstance!.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen) ToggleFullscreen();
            ViewModel.ShowControlsCommand.Execute(null);
            e.Handled = true;
        }
        else if (!_ready) return;
        else if (focusedControl is TextBox) return;
        else if (e.Key == VirtualKey.Space && focusedControl is not Button)
        {
            await ViewModel.TogglePlayPauseCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        else if (focusedControl is not Slider && e.Key is VirtualKey.Left or VirtualKey.Right)
        {
            await ViewModel.SeekToAsync(ViewModel.State.PositionSeconds + (e.Key == VirtualKey.Left ? -5 : 5));
            e.Handled = true;
        }
        RestartAutoHideTimer();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.HasTerminalFailure) && ViewModel.HasTerminalFailure)
        {
            _ready = false;
            _autoHideTimer?.Stop();
            _seekTimer?.Stop();
            _volumeTimer?.Stop();
            OpenMediaPanel.IsEnabled = TransportControls.IsEnabled = false;
            return;
        }
        if (e.PropertyName != nameof(PlayerViewModel.State)) return;
        WelcomePanel.Visibility = string.IsNullOrEmpty(ViewModel.State.CurrentUrl) ? Visibility.Visible : Visibility.Collapsed;
        SyncSliders();
    }

    private void SyncSliders()
    {
        _updatingSliders = true;
        try
        {
            if (!_scrubbing && _seekTimer?.IsRunning != true)
            {
                Timeline.Maximum = Math.Max(1, ViewModel.State.DurationSeconds);
                Timeline.Value = Math.Clamp(ViewModel.State.PositionSeconds, 0, Timeline.Maximum);
            }
            if (_volumeTimer?.IsRunning != true) VolumeSlider.Value = ViewModel.State.Volume;
        }
        finally { _updatingSliders = false; }
    }

    private void OnTimelinePressed(object sender, PointerRoutedEventArgs e)
    {
        _scrubbing = true;
        _autoHideTimer?.Stop();
    }

    private void OnTimelineReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrubbing) return;
        _scrubbing = false;
        _seekTimer?.Stop();
        _seekTimer?.Start();
        RestartAutoHideTimer();
    }

    private void OnTimelineValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _updatingSliders || _scrubbing) return;
        _seekTimer?.Stop();
        _seekTimer?.Start();
    }

    private void OnVolumeValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _updatingSliders) return;
        _volumeTimer?.Stop();
        _volumeTimer?.Start();
    }

    private void CreateTimers()
    {
        _autoHideTimer = DispatcherQueue.CreateTimer();
        _autoHideTimer.Interval = TimeSpan.FromSeconds(3);
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += (_, _) =>
        {
            if (!_scrubbing && FocusManager.GetFocusedElement(XamlRoot) is not TextBox) ViewModel.OnIdleTimeout();
        };
        _seekTimer = DispatcherQueue.CreateTimer();
        _seekTimer.Interval = TimeSpan.FromMilliseconds(180);
        _seekTimer.IsRepeating = false;
        _seekTimer.Tick += async (_, _) => { if (_ready) await ViewModel.SeekToAsync(Timeline.Value); };
        _volumeTimer = DispatcherQueue.CreateTimer();
        _volumeTimer.Interval = TimeSpan.FromMilliseconds(100);
        _volumeTimer.IsRepeating = false;
        _volumeTimer.Tick += async (_, _) => { if (_ready) await ViewModel.ChangeVolumeAsync(VolumeSlider.Value); };
    }

    private void RestartAutoHideTimer()
    {
        _autoHideTimer?.Stop();
        if (!_scrubbing) _autoHideTimer?.Start();
    }
}

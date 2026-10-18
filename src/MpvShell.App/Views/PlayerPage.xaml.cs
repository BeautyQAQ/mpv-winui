using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using MpvShell.App.Services;
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
    private DispatcherQueueTimer? _interactionTimer;
    private Point? _dragStartPoint;
    private Pointer? _videoPointer;
    private Pointer? _timelinePointer;
    private object? _interactionFocusOwner;
    private bool _initializeRequested;
    private bool _ready;
    private bool _updatingSliders;
    private bool _speedMenuOpen;
    private bool _filePickerOpen;
    private bool _windowActive = true;
    private bool _pointerOverControls;
    private bool _lastCanAutoHide;
    private OverlayKind _lastOverlay;
    private Control? _overlayFocusReturn;
    private FocusState _overlayFocusState;
    private bool _lastIsStopped;
    private bool _wasInteracting;
    private bool _hadTimelinePreview;
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
        _lastIsStopped = ViewModel.State.IsStopped;
        _wasInteracting = ViewModel.HasActiveInteraction;
        _hadTimelinePreview = ViewModel.HasTimelinePreview;
        ViewModel.SetUiDispatcher(action =>
        {
            if (DispatcherQueue.HasThreadAccess) action();
            else DispatcherQueue.TryEnqueue(() => action());
        });
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = ViewModel;
        RootGrid.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnAnyPointerActivity), true);
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnAnyPointerActivity), true);
        ViewModel.RevealControls();
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
        if (_releaseTask is not null) return;

        // 启动媒体独立于页面资源初始化：关闭先等待资源可释放，再由 VM 取消媒体加载。
        // 若把此等待放进 _initializationTask，页面释放会等网络超时，VM.DisposeAsync 无法及时取消它。
        Task? startupLoad = null;
        if (_ready)
        {
            var mediaArgument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(mediaArgument) && Interlocked.Exchange(ref _startupMediaClaimed, 1) == 0)
                startupLoad = OpenMediaAsync(mediaArgument);
        }
#if DEBUG
        await Diagnostics.AppRecoveryProbe.RunIfRequestedAsync(this, ViewModel, _videoSurfaceRenderer,
            ((App)Application.Current).Services.GetRequiredService<IMpvPlayerSession>(),
            ((MainWindow)((App)Application.Current).MainWindowInstance!).ShutdownPlayerAsync, _ready,
            () => OpenMediaPanel.IsEnabled || TransportControls.IsEnabled, startupLoad);
#endif
        if (startupLoad is not null) await startupLoad;
    }

    private async Task InitializePlayerAsync()
    {
        try
        {
            await ViewModel.InitializeAsync();
            if (_releaseTask is not null || !ViewModel.IsInitialized || ViewModel.HasTerminalFailure) return;
            var session = ((App)Application.Current).Services.GetRequiredService<MpvPlayerSession>();
            await _videoSurfaceRenderer.InitializeAsync(session, CancellationToken.None);
            if (_releaseTask is not null || ViewModel.HasTerminalFailure) return;
            await _videoSurfaceRenderer.AttachAsync(VideoSurface, CancellationToken.None);
            if (_releaseTask is not null || ViewModel.HasTerminalFailure) return;
            // 页面重新挂载或同进程内重建页面后恢复会话挂起的视频；首次启动时为 no-op。
            await session.ResumeVideoAfterDetachAsync(CancellationToken.None);
            if (_releaseTask is not null || ViewModel.HasTerminalFailure) return;
            _ready = true;
            OpenMediaPanel.IsEnabled = TransportControls.IsEnabled = true;
            XamlRoot.Changed += OnXamlRootChanged;
            var window = ((App)Application.Current).MainWindowInstance!;
            _displayOutputMonitor = new DisplayOutputMonitor(WinRT.Interop.WindowNative.GetWindowHandle(window));
            _displayOutputMonitor.Changed += OnDisplayOutputChanged;
            window.AppWindow.Changed += OnAppWindowChanged;
            window.Activated += OnWindowActivated;
            QueueOutputRefresh();
            await _outputRefreshTask;
            if (_releaseTask is not null || ViewModel.HasTerminalFailure) return;
            CreateTimers();
            SyncSliders();
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
        _interactionTimer?.Stop();
        CancelInputInteractions();
        if (_initializationTask is not null) await _initializationTask;
        if (XamlRoot is not null) XamlRoot.Changed -= OnXamlRootChanged;
        if (((App)Application.Current).MainWindowInstance is { } window)
        {
            window.AppWindow.Changed -= OnAppWindowChanged;
            window.Activated -= OnWindowActivated;
        }
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
        _interactionTimer?.Stop();
        CancelInputInteractions();
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
        if (args.DidPresenterChange)
        {
            FullscreenButton.Content = sender.Presenter.Kind == AppWindowPresenterKind.FullScreen ? "退出全屏" : "全屏";
            CancelInputInteractions();
            ViewModel.RevealControls();
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (!_windowActive)
        {
            _pointerOverControls = false;
            CancelInputInteractions();
        }
        else ViewModel.RevealControls();
        RestartAutoHideTimer();
    }

    private void OnPlayerGotFocus(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ViewModel.RevealControls();
        RestartAutoHideTimer();
    }

    private void OnPlayerLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        // Focus() 触发的旧控件 LostFocus 可能稍后才冒泡；新焦点已属于当前输入时不取消。
        if (_interactionFocusOwner is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), _interactionFocusOwner)) return;
        CancelInputInteractions();
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
        if (!_ready || _releaseTask is not null) return;
        ViewModel.UrlText = source;
        await ViewModel.OpenUrlCommand.ExecuteAsync(null);
        if (_ready && _releaseTask is null) RestartAutoHideTimer();
    }

    private async void OnOpenFileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _filePickerOpen = true;
            CancelInputInteractions();
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
        finally { _filePickerOpen = false; RestartAutoHideTimer(); }
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

    private async void OnTrackOptionClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackOption option) await ViewModel.SelectTrackOptionCommand.ExecuteAsync(option);
    }

    private void OnSpeedMenuOpened(object sender, object e)
    {
        _speedMenuOpen = true;
        CancelInputInteractions();
        _autoHideTimer?.Stop();
        ViewModel.RevealControls();
    }

    private void OnSpeedMenuClosed(object sender, object e)
    {
        _speedMenuOpen = false;
        RestartAutoHideTimer();
    }

    private async void OnSpeedClicked(object sender, RoutedEventArgs e)
    {
        if (_ready && sender is MenuFlyoutItem { Tag: string value } &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
            await ViewModel.ChangeSpeedAsync(speed);
    }

    private void OnAnyPointerActivity(object sender, PointerRoutedEventArgs e)
    {
        // 触控没有悬停，抬起后不会留下鼠标式的永久自动隐藏阻挡。
        var point = e.GetCurrentPoint(RootGrid).Position;
        _pointerOverControls = e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse &&
            (ContainsPoint(HeaderChrome, point) || ContainsPoint(TransportChrome, point));
        ViewModel.RevealControls();
        RestartAutoHideTimer();
    }

    private bool ContainsPoint(FrameworkElement element, Point point)
    {
        if (element.Visibility != Visibility.Visible) return false;
        return element.TransformToVisual(RootGrid).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)).Contains(point);
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverControls = false;
        ViewModel.ClearTimelineHover();
        RestartAutoHideTimer();
    }

    private void OnVideoPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_ready || _videoPointer is not null || _timelinePointer is not null || !IsPrimaryContact(e, InteractionSurface)) return;
        if (ViewModel.State.CurrentOverlay != OverlayKind.None)
        {
            ViewModel.ShowControlsCommand.Execute(null);
            e.Handled = true;
            return;
        }
        Focus(FocusState.Programmatic);
        if (!ViewModel.BeginVideoGesture()) return;
        _interactionFocusOwner = this;
        _dragStartPoint = e.GetCurrentPoint(InteractionSurface).Position;
        _videoPointer = e.Pointer;
        if (!InteractionSurface.CapturePointer(e.Pointer))
        {
            CancelInputInteractions();
            return;
        }
        e.Handled = true;
        ViewModel.RevealControls();
        RestartAutoHideTimer();
    }

    private void OnVideoPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_ready || _videoPointer?.PointerId != e.Pointer.PointerId || _dragStartPoint is null) return;
        var current = e.GetCurrentPoint(InteractionSurface).Position;
        ViewModel.UpdateVideoGesture(current.X - _dragStartPoint.Value.X, current.Y - _dragStartPoint.Value.Y);
        e.Handled = true;
    }

    private async void OnVideoPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_videoPointer?.PointerId != e.Pointer.PointerId) return;
        OnVideoPointerMoved(sender, e);
        // 正常 release 先结束本地所有权；同步触发的 CaptureLost 不能取消或重复提交。
        _videoPointer = null;
        _dragStartPoint = null;
        InteractionSurface.ReleasePointerCaptures();
        await ViewModel.CommitVideoGestureAsync();
        e.Handled = true;
        RestartAutoHideTimer();
    }

    private void OnVideoPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_videoPointer?.PointerId == e.Pointer.PointerId) CancelInputInteractions();
    }

    private void OnVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.State.CurrentOverlay != OverlayKind.None || _speedMenuOpen) return;
        ToggleFullscreen();
        e.Handled = true;
    }
    private void OnFullscreenClicked(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        CancelInputInteractions();
        var window = ((App)Application.Current).MainWindowInstance!;
        var isFullscreen = window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        window.AppWindow.SetPresenter(isFullscreen ? AppWindowPresenterKind.Default : AppWindowPresenterKind.FullScreen);
        FullscreenButton.Content = isFullscreen ? "全屏" : "退出全屏";
        ViewModel.RevealControls();
        RestartAutoHideTimer();
    }

    private void OnPlayerPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_releaseTask is not null) return;
        if (e.Key == VirtualKey.F11 && !_speedMenuOpen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (e.Key != VirtualKey.Escape) return;
        var isFullscreen = ((App)Application.Current).MainWindowInstance!.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        var action = PlayerChromePolicy.ResolveEscape(ViewModel.HasActiveInteraction, ViewModel.HasTimelinePreview,
            _speedMenuOpen || _filePickerOpen, ViewModel.State.CurrentOverlay != OverlayKind.None, isFullscreen);
        switch (action)
        {
            case PlayerEscapeAction.CancelInteraction: CancelInputInteractions(); break;
            case PlayerEscapeAction.CloseOverlay: ViewModel.ShowControlsCommand.Execute(null); break;
            case PlayerEscapeAction.ExitFullscreen: ToggleFullscreen(); break;
            // 原生 Flyout/文件选择器负责自己的 Esc；不可让同一次按键继续关闭下层。
            default: return;
        }
        e.Handled = true;
        RestartAutoHideTimer();
    }

    private async void OnPlayerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_releaseTask is not null) return;
        // Alt+Space 属于窗口系统菜单，Ctrl/Alt 组合键不可兼作播放快捷键。
        if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0 ||
            (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) return;
        var focusedControl = FocusManager.GetFocusedElement(XamlRoot);
        if (e.Handled || focusedControl is TextBox or MenuFlyoutItemBase || _speedMenuOpen || ViewModel.State.CurrentOverlay != OverlayKind.None) return;
        if (!_ready) return;
        if (e.Key == VirtualKey.Space && focusedControl is not ButtonBase && focusedControl is not Slider)
        {
            await ViewModel.TogglePlayPauseCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        else if (focusedControl is not Slider && e.Key is VirtualKey.Left or VirtualKey.Right)
        {
            ViewModel.QueueTimelineSeek(ViewModel.TimelineValue + (e.Key == VirtualKey.Left ? -5 : 5));
            if (ViewModel.HasActiveInteraction) _interactionFocusOwner = focusedControl;
            e.Handled = true;
        }
        RestartAutoHideTimer();
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.IsLoading) or nameof(PlayerViewModel.IsBuffering))
            RestartAutoHideTimer();
        if (e.PropertyName == nameof(PlayerViewModel.HasTerminalFailure) && ViewModel.HasTerminalFailure)
        {
            _ready = false;
            _autoHideTimer?.Stop();
            _interactionTimer?.Stop();
            CancelInputInteractions();
            OpenMediaPanel.IsEnabled = TransportControls.IsEnabled = false;
            return;
        }
        if (e.PropertyName is nameof(PlayerViewModel.HasActiveInteraction) or nameof(PlayerViewModel.HasTimelinePreview))
        {
            var active = ViewModel.HasActiveInteraction;
            var preview = ViewModel.HasTimelinePreview;
            if (_wasInteracting && !active) ReleaseInputPointers();
            var changed = _wasInteracting != active || _hadTimelinePreview != preview;
            _wasInteracting = active;
            _hadTimelinePreview = preview;
            // 播放位置刷新也会通知派生属性；只有交互状态真正切换才重置闲置时钟。
            if (changed) RestartAutoHideTimer();
        }
        if (e.PropertyName is nameof(PlayerViewModel.CanSeek) or nameof(PlayerViewModel.IsStopping) or
            nameof(PlayerViewModel.TimelineValue) or nameof(PlayerViewModel.TimelineMaximum) or nameof(PlayerViewModel.VolumeValue))
        {
            if (!ViewModel.CanSeek && _timelinePointer is not null) CancelInputInteractions();
            SyncSliders();
        }
        if (e.PropertyName != nameof(PlayerViewModel.State)) return;
        var becameStopped = !_lastIsStopped && ViewModel.State.IsStopped;
        _lastIsStopped = ViewModel.State.IsStopped;
        UpdateOverlayFocus();
        var canAutoHide = CanAutoHide();
        if (_lastCanAutoHide != canAutoHide) RestartAutoHideTimer();
        SyncSliders();
        if (becameStopped && _ready && _releaseTask is null)
        {
            try { await _videoSurfaceRenderer.RequestRedrawAsync(CancellationToken.None); }
            catch (Exception ex)
            {
                if (_releaseTask is null) OnRenderingFailed($"停止后刷新视频画面失败：{ex.Message}");
            }
        }
    }

    private void SyncSliders()
    {
        _updatingSliders = true;
        try
        {
            Timeline.IsEnabled = _ready && ViewModel.CanSeek;
            Timeline.Maximum = ViewModel.TimelineMaximum;
            Timeline.Value = ViewModel.TimelineValue;
            VolumeSlider.Value = ViewModel.VolumeValue;
        }
        finally { _updatingSliders = false; }
    }

    private void OnTimelinePressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_ready || !ViewModel.CanSeek || _timelinePointer is not null || _videoPointer is not null || !IsPrimaryContact(e, TimelineHitTarget) ||
            !TryGetTimelineTarget(e, out var target)) return;
        Timeline.Focus(FocusState.Pointer);
        if (!ViewModel.BeginTimelineScrub(target)) return;
        _interactionFocusOwner = Timeline;
        _timelinePointer = e.Pointer;
        if (!TimelineHitTarget.CapturePointer(e.Pointer))
        {
            CancelInputInteractions();
            return;
        }
        e.Handled = true;
        RestartAutoHideTimer();
    }

    private async void OnTimelineReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_timelinePointer?.PointerId != e.Pointer.PointerId) return;
        if (TryGetTimelineTarget(e, out var target)) ViewModel.UpdateTimelineScrub(target);
        _timelinePointer = null;
        TimelineHitTarget.ReleasePointerCaptures();
        await ViewModel.CommitTimelineScrubAsync();
        e.Handled = true;
        RestartAutoHideTimer();
    }

    private void OnTimelineCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_timelinePointer?.PointerId == e.Pointer.PointerId) CancelInputInteractions();
    }

    private void OnTimelinePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_ready || !ViewModel.CanSeek || !TryGetTimelineTarget(e, out var target)) return;
        if (_timelinePointer?.PointerId == e.Pointer.PointerId)
        {
            ViewModel.UpdateTimelineScrub(target);
            e.Handled = true;
        }
        else if (_timelinePointer is null && !e.GetCurrentPoint(TimelineHitTarget).IsInContact)
            ViewModel.PreviewTimeline(target);
        RestartAutoHideTimer();
    }

    private void OnTimelinePointerExited(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.ClearTimelineHover();
        RestartAutoHideTimer();
    }

    private bool TryGetTimelineTarget(PointerRoutedEventArgs e, out double target)
    {
        target = 0;
        // 使用当前模板的实际轨道及 Thumb 尺寸，跟随主题、DPI 和布局；不猜测固定边距。
        var track = FindVisualChild<FrameworkElement>(Timeline, "HorizontalTemplate");
        var thumb = FindVisualChild<Thumb>(Timeline, "HorizontalThumb");
        if (track is null || thumb is null || track.ActualWidth <= thumb.ActualWidth) return false;
        var point = e.GetCurrentPoint(track).Position;
        var proportion = (point.X - thumb.ActualWidth / 2) / (track.ActualWidth - thumb.ActualWidth);
        if (Timeline.IsDirectionReversed ^ (Timeline.FlowDirection == FlowDirection.RightToLeft)) proportion = 1 - proportion;
        target = Timeline.Minimum + Math.Clamp(proportion, 0, 1) * (Timeline.Maximum - Timeline.Minimum);
        return double.IsFinite(target);
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element && element.Name == name) return element;
            if (FindVisualChild<T>(child, name) is { } descendant) return descendant;
        }
        return null;
    }

    private static bool IsPrimaryContact(PointerRoutedEventArgs e, UIElement relativeTo)
    {
        var point = e.GetCurrentPoint(relativeTo);
        return point.IsInContact && !point.Properties.IsRightButtonPressed && !point.Properties.IsMiddleButtonPressed;
    }

    private void ReleaseInputPointers()
    {
        // CaptureLost 可能同步重入，因此先清空标识再归还系统捕获。
        _timelinePointer = null;
        _videoPointer = null;
        _dragStartPoint = null;
        _interactionFocusOwner = null;
        TimelineHitTarget.ReleasePointerCaptures();
        InteractionSurface.ReleasePointerCaptures();
    }

    private void CancelInputInteractions()
    {
        ReleaseInputPointers();
        ViewModel.CancelInputInteractions();
        SyncSliders();
        RestartAutoHideTimer();
    }

    private void OnTimelineValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _updatingSliders || !ViewModel.CanSeek) return;
        // Pointer 输入由透明适配层负责；Slider 原生键盘与辅助技术修改共用协调层队列。
        ViewModel.QueueTimelineSeek(e.NewValue);
        if (ViewModel.HasActiveInteraction) _interactionFocusOwner = Timeline;
        RestartAutoHideTimer();
    }

    private void OnVolumeValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _updatingSliders) return;
        ViewModel.QueueVolumeChange(e.NewValue);
        if (ViewModel.HasActiveInteraction) _interactionFocusOwner = VolumeSlider;
        RestartAutoHideTimer();
    }

    private void CreateTimers()
    {
        _autoHideTimer = DispatcherQueue.CreateTimer();
        _autoHideTimer.Interval = TimeSpan.FromSeconds(3);
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += (_, _) =>
        {
            if (CanAutoHide()) ViewModel.OnIdleTimeout();
        };
        _interactionTimer = DispatcherQueue.CreateTimer();
        _interactionTimer.Interval = TimeSpan.FromMilliseconds(50);
        _interactionTimer.IsRepeating = true;
        _interactionTimer.Tick += async (_, _) => { if (_ready) await ViewModel.ProcessInteractionTickAsync(); };
        _interactionTimer.Start();
    }

    private void RestartAutoHideTimer()
    {
        _autoHideTimer?.Stop();
        _lastCanAutoHide = CanAutoHide();
        if (_lastCanAutoHide) _autoHideTimer?.Start();
    }

    private bool CanAutoHide()
    {
        if (!_ready || XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as Control;
        var keyboardFocus = focused is not null && !ReferenceEquals(focused, this) &&
            (focused is TextBox || focused.FocusState == FocusState.Keyboard);
        return PlayerChromePolicy.CanAutoHide(new(_ready, _windowActive, ViewModel.State.IsPlaying,
            ViewModel.State.IsStopped, ViewModel.State.CurrentOverlay != OverlayKind.None,
            ViewModel.HasActiveInteraction, ViewModel.HasTimelinePreview, _speedMenuOpen || _filePickerOpen || ViewModel.IsLoading || ViewModel.IsBuffering,
            _pointerOverControls, keyboardFocus));
    }

    private void UpdateOverlayFocus()
    {
        var overlay = ViewModel.State.CurrentOverlay;
        if (overlay == _lastOverlay) return;
        var previous = _lastOverlay;
        _lastOverlay = overlay;
        if (overlay != OverlayKind.None)
        {
            if (previous == OverlayKind.None)
            {
                _overlayFocusReturn = FocusManager.GetFocusedElement(XamlRoot) as Control;
                _overlayFocusState = _overlayFocusReturn?.FocusState == FocusState.Keyboard ? FocusState.Keyboard : FocusState.Programmatic;
            }
            // 绑定可见性在本次通知之后生效，再调度焦点进入面板。
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_releaseTask is not null || ViewModel.State.CurrentOverlay != overlay) return;
                var closeButton = overlay switch
                {
                    OverlayKind.Tracks => CloseTracksButton,
                    OverlayKind.InfoPanel => CloseInfoButton,
                    _ => CloseRecentButton,
                };
                closeButton.Focus(FocusState.Programmatic);
            });
        }
        else
        {
            var target = _overlayFocusReturn;
            _overlayFocusReturn = null;
            if (target is null || !target.Focus(_overlayFocusState)) Focus(FocusState.Programmatic);
        }
        RestartAutoHideTimer();
    }
}

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.App.Views;
using Windows.Graphics;

namespace MpvShell.App;

public sealed partial class MainWindow : Window
{
    private readonly PlayerViewModel _playerViewModel;
    private readonly nint _windowHandle;
    private InputNonClientPointerSource? _nonClientPointerSource;
    private PointInt32? _pointerPositionBeforeChromeChange;
    private bool? _appliedTitleBarVisible;
    private bool _applyingTitleBarVisibility;
    private bool _isNonClientPointerPressed;
    private bool _isMovingOrSizing;
    private PointerDeviceType _lastPointerDeviceType = PointerDeviceType.Mouse;
    private PointInt32? _mousePositionAtNonMouseInput;
    private bool _lastTitleBarInteracting;
    private bool _isWindowActive = true;
    private bool _chromeNotificationsDetached;
    private bool _closing;
    private bool _canClose;
    private Task? _shutdownTask;
    public MainWindow()
    {
        InitializeComponent();
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "Veyra.ico"));
        _playerViewModel = ((App)Application.Current).Services.GetRequiredService<PlayerViewModel>();
        RootFrame.ActualThemeChanged += OnActualThemeChanged;
        UpdateTitleBarTheme();
        // AppWindow.Resize 使用物理像素。进程已声明 PerMonitorV2，按当前显示器 DPI 换算，
        // 使 150% 缩放的 4K 屏和 100% 的 1080p 屏得到相同的逻辑窗口大小。
        var dpi = GetDpiForWindow(_windowHandle);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        AppWindow.Resize(new SizeInt32((int)Math.Round(1120 * scale), (int)Math.Round(760 * scale)));
        ApplyMinimumWindowSize();
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnClosing;
        Activated += OnWindowActivated;
        Closed += OnClosed;
        _playerViewModel.PropertyChanged += OnPlayerViewModelPropertyChanged;
        InitializeNonClientPointerSource();
        // 页面通过 Frame 导航承载：卸载/重新挂载遵循 WinUI 页面生命周期，
        // 页面释放渲染资源但不销毁共享播放会话（见 PlayerPage.ReleaseAsync）。
        RootFrame.Navigate(typeof(PlayerPage));
        ApplyTitleBarVisibility();
    }

    /// <summary>原生标题栏、边框悬停或移动/缩放窗口时，页面应保留播放控件。</summary>
    public bool IsTitleBarInteracting => !_chromeNotificationsDetached && _isWindowActive
        && (_isNonClientPointerPressed || _isMovingOrSizing || IsPointerInNonClientArea());

    public event EventHandler? TitleBarInteractionChanged;

    /// <summary>只接收真实指针活动；触控或笔输入后，不把遗留鼠标位置当作持续悬停。</summary>
    public void NotifyPointerInput(PointerDeviceType deviceType)
    {
        if (_chromeNotificationsDetached) return;
        _lastPointerDeviceType = deviceType;
        _mousePositionAtNonMouseInput = deviceType != PointerDeviceType.Mouse && GetCursorPos(out var point)
            ? point : null;
        UpdateTitleBarInteraction();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointInt32 point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint hwnd, ref PointInt32 point);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly bool Contains(PointInt32 point) =>
            point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
    }

    private bool IsPointerInNonClientArea()
    {
        // 默认系统标题栏不保证发送 InputNonClientPointerSource 的悬停事件。
        // 每次闲置判定读取真实屏幕位置；DPI 感知进程使用 Win32 统一坐标转换。
        if (AppWindow.Presenter is not OverlappedPresenter || !GetCursorPos(out var point)
            || !GetWindowRect(_windowHandle, out var windowRect) || !windowRect.Contains(point)) return false;
        // 默认标题栏可能没有指针事件：触控后鼠标屏幕位置实际改变，也应恢复鼠标悬停保护。
        // 此处仅比较快照，不修改输入类型，保证闲置判定与可见性读取没有状态副作用。
        if (_lastPointerDeviceType != PointerDeviceType.Mouse &&
            (_mousePositionAtNonMouseInput is not { } previous || (previous.X == point.X && previous.Y == point.Y))) return false;
        if (!GetClientRect(_windowHandle, out var clientRect) || !ScreenToClient(_windowHandle, ref point)) return false;
        return !clientRect.Contains(point);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => UpdateTitleBarTheme();

    private void UpdateTitleBarTheme() => AppWindow.TitleBar.PreferredTheme =
        RootFrame.ActualTheme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange) return;
        _appliedTitleBarVisible = null;
        ResetTitleBarInteraction();
        ApplyMinimumWindowSize();
        ApplyTitleBarVisibility();
    }

    private void OnPlayerViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PlayerViewModel.State)) return;
        // 播放状态刷新也覆盖默认系统标题栏缺少指针事件的情况；离开后让页面重新计时。
        UpdateTitleBarInteraction();
        ApplyTitleBarVisibility();
    }

    private void ApplyTitleBarVisibility()
    {
        if (_chromeNotificationsDetached || _applyingTitleBarVisibility
            || AppWindow.Presenter is not OverlappedPresenter presenter) return;

        var visible = _playerViewModel.State.AreControlsVisible || IsTitleBarInteracting;
        if (_appliedTitleBarVisible == visible && presenter.HasTitleBar == visible && presenter.HasBorder
            && (!visible || (!ExtendsContentIntoTitleBar && !AppWindow.TitleBar.ExtendsContentIntoTitleBar))) return;

        // 原生标题栏改变客户区尺寸，可能产生坐标未移动的 PointerEntered/PointerMoved。
        // 记录屏幕坐标，防止这些布局事件重新显示刚隐藏的控件。
        _pointerPositionBeforeChromeChange = GetCursorPos(out var point) ? point : null;
        _applyingTitleBarVisibility = true;
        try
        {
            var before = $"hasTitleBar={presenter.HasTitleBar}, windowExtends={ExtendsContentIntoTitleBar}, appWindowExtends={AppWindow.TitleBar.ExtendsContentIntoTitleBar}";
            presenter.SetBorderAndTitleBar(true, visible);
            var afterPresenter = $"hasTitleBar={presenter.HasTitleBar}, windowExtends={ExtendsContentIntoTitleBar}, appWindowExtends={AppWindow.TitleBar.ExtendsContentIntoTitleBar}";
            if (visible)
            {
                // 仅恢复 presenter 会留下默认自定义标题栏（只有覆盖客户区的三个按钮）。
                // 同时恢复 WinUI 与 AppWindow 的系统标题栏状态，再重新应用系统主题。
                ExtendsContentIntoTitleBar = false;
                AppWindow.TitleBar.ResetToDefault();
                UpdateTitleBarTheme();
            }
            _appliedTitleBarVisible = visible;
            Trace.WriteLine($"[app] title-bar visible={visible}; before: {before}; presenter: {afterPresenter}; final: hasTitleBar={presenter.HasTitleBar}, windowExtends={ExtendsContentIntoTitleBar}, appWindowExtends={AppWindow.TitleBar.ExtendsContentIntoTitleBar}");
        }
        finally
        {
            _applyingTitleBarVisibility = false;
        }

        if (visible != (_playerViewModel.State.AreControlsVisible || IsTitleBarInteracting))
            DispatcherQueue.TryEnqueue(ApplyTitleBarVisibility);
    }

    private void InitializeNonClientPointerSource()
    {
        _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        _nonClientPointerSource.PointerEntered += OnNonClientPointerMoved;
        _nonClientPointerSource.PointerMoved += OnNonClientPointerMoved;
        _nonClientPointerSource.PointerExited += OnNonClientPointerExited;
        _nonClientPointerSource.PointerPressed += OnNonClientPointerPressed;
        _nonClientPointerSource.PointerReleased += OnNonClientPointerReleased;
        _nonClientPointerSource.EnteredMoveSize += OnEnteredMoveSize;
        _nonClientPointerSource.ExitedMoveSize += OnExitedMoveSize;
    }

    private static bool IsInNonClientRegion(NonClientPointerEventArgs args) =>
        args.IsPointInRegion && args.RegionKind != NonClientRegionKind.Passthrough;

    private void OnNonClientPointerMoved(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        if (_chromeNotificationsDetached || !_isWindowActive || _applyingTitleBarVisibility) return;

        if (args.PointerDeviceType == PointerDeviceType.Mouse && GetCursorPos(out var current))
        {
            if (_pointerPositionBeforeChromeChange is { } previous && previous.X == current.X && previous.Y == current.Y) return;
            // 触控后的同坐标鼠标事件也可能来自布局，不能据此重新启用遗留鼠标悬停。
            if (_lastPointerDeviceType != PointerDeviceType.Mouse && _mousePositionAtNonMouseInput is { } nonMousePosition &&
                nonMousePosition.X == current.X && nonMousePosition.Y == current.Y) return;
            _pointerPositionBeforeChromeChange = null;
        }

        NotifyPointerInput(args.PointerDeviceType);
    }

    private void OnNonClientPointerPressed(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        if (!IsInNonClientRegion(args)) return;
        _pointerPositionBeforeChromeChange = null;
        _isNonClientPointerPressed = true;
        NotifyPointerInput(args.PointerDeviceType);
    }

    private void OnNonClientPointerReleased(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        _isNonClientPointerPressed = false;
        NotifyPointerInput(args.PointerDeviceType);
    }

    private void OnNonClientPointerExited(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        // PointerReleased 仅在非客户区内释放时触发；离开时也释放按下状态。
        // 原生拖动则由 EnteredMoveSize/ExitedMoveSize 独立保留。
        _isNonClientPointerPressed = false;
        UpdateTitleBarInteraction();
    }

    private void OnEnteredMoveSize(InputNonClientPointerSource sender, EnteredMoveSizeEventArgs args)
    {
        _isMovingOrSizing = true;
        UpdateTitleBarInteraction();
    }

    private void OnExitedMoveSize(InputNonClientPointerSource sender, ExitedMoveSizeEventArgs args)
    {
        _isMovingOrSizing = false;
        _isNonClientPointerPressed = false;
        UpdateTitleBarInteraction();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (!_isWindowActive) ResetTitleBarInteraction();
    }

    private void ResetTitleBarInteraction()
    {
        _isNonClientPointerPressed = false;
        _isMovingOrSizing = false;
        UpdateTitleBarInteraction();
    }

    private void UpdateTitleBarInteraction()
    {
        if (_chromeNotificationsDetached) return;
        var interacting = IsTitleBarInteracting;
        if (_lastTitleBarInteracting == interacting) return;
        _lastTitleBarInteracting = interacting;
        if (interacting) _playerViewModel.RevealControls();
        TitleBarInteractionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        DetachChromeNotifications();
        AppWindow.Closing -= OnClosing;
        Closed -= OnClosed;
    }

    private void DetachChromeNotifications()
    {
        if (_chromeNotificationsDetached) return;
        _chromeNotificationsDetached = true;
        _playerViewModel.PropertyChanged -= OnPlayerViewModelPropertyChanged;
        RootFrame.ActualThemeChanged -= OnActualThemeChanged;
        AppWindow.Changed -= OnAppWindowChanged;
        Activated -= OnWindowActivated;
        if (_nonClientPointerSource is { } source)
        {
            source.PointerEntered -= OnNonClientPointerMoved;
            source.PointerMoved -= OnNonClientPointerMoved;
            source.PointerExited -= OnNonClientPointerExited;
            source.PointerPressed -= OnNonClientPointerPressed;
            source.PointerReleased -= OnNonClientPointerReleased;
            source.EnteredMoveSize -= OnEnteredMoveSize;
            source.ExitedMoveSize -= OnExitedMoveSize;
            _nonClientPointerSource = null;
        }
        _lastTitleBarInteracting = false;
    }

    private void ApplyMinimumWindowSize()
    {
        // 全屏 presenter 不约束尺寸；恢复窗口时重新应用，避免切换后能缩到控件不可用。
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        presenter.PreferredMinimumWidth = PlayerChromePolicy.MinimumWindowWidth;
        presenter.PreferredMinimumHeight = PlayerChromePolicy.MinimumWindowHeight;
    }

    /// <summary>
    /// 完整的播放器关闭：先释放当前页面（挂起视频、释放渲染资源），再销毁共享播放会话。
    /// 幂等；窗口关闭与恢复验收探针共用同一入口。
    /// </summary>
    public Task ShutdownPlayerAsync() => _shutdownTask ??= ShutdownPlayerCoreAsync();

    private async Task ShutdownPlayerCoreAsync()
    {
        if (RootFrame.Content is PlayerPage page)
        {
            try { await page.ReleaseAsync(); }
            catch (Exception ex) { Trace.WriteLine($"[app] 关闭页面资源失败：{ex}"); }
        }
        try { await _playerViewModel.DisposeAsync(); }
        catch (Exception ex) { Trace.WriteLine($"[app] 销毁播放器会话失败：{ex}"); }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_canClose) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        DetachChromeNotifications();
        try { await ShutdownPlayerAsync(); }
        catch (Exception ex) { Trace.WriteLine($"关闭播放器失败：{ex}"); }
        finally
        {
            _canClose = true;
            Close();
        }
    }

#if DEBUG
    /// <summary>恢复验收探针使用真实导航验证页面卸载/重新挂载。</summary>
    internal Microsoft.UI.Xaml.Controls.Frame DebugRootFrame => RootFrame;
#endif
}

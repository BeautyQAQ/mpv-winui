using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MpvShell.App.ViewModels;
using MpvShell.Rendering.WinUI;
using Windows.Foundation;

namespace MpvShell.App.Views;

public sealed partial class PlayerPage : Page
{
    private DispatcherQueueTimer? _autoHideTimer;
    private Point? _dragStartPoint;
    private bool _initializeRequested;
    private readonly D3D11VideoSurfaceRenderer _videoSurfaceRenderer;
    public PlayerViewModel ViewModel { get; }

    public PlayerPage()
    {
        InitializeComponent();
        ViewModel = ((App)Application.Current).Services.GetRequiredService<PlayerViewModel>();
        _videoSurfaceRenderer = new D3D11VideoSurfaceRenderer();
        DataContext = ViewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initializeRequested)
        {
            return;
        }

        _initializeRequested = true;

        await ViewModel.InitializeAsync();

        // P0-06：绑定 D3D11 Composition SwapChain 到视频表面（不使用 libmpv）。
        await _videoSurfaceRenderer.InitializeAsync(null!, CancellationToken.None);
        await _videoSurfaceRenderer.AttachAsync(VideoSurface, CancellationToken.None);

        EnsureAutoHideTimer();
        RestartAutoHideTimer();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        await _videoSurfaceRenderer.DetachAsync(CancellationToken.None);
    }

    private async void OnVideoSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initializeRequested || e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        await _videoSurfaceRenderer.ResizeAsync(
            new VideoSurfaceSize(
                e.NewSize.Width,
                e.NewSize.Height,
                VideoSurface.RasterizationScale),
            CancellationToken.None);
    }

    private void OnAnyPointerActivity(object sender, PointerRoutedEventArgs e)
    {
        RestartAutoHideTimer();
    }

    private void OnVideoPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragStartPoint = e.GetCurrentPoint(InteractionSurface).Position;
        ViewModel.ShowControlsCommand.Execute(null);
        RestartAutoHideTimer();
    }

    private async void OnVideoPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        RestartAutoHideTimer();

        if (_dragStartPoint is null || !e.GetCurrentPoint(InteractionSurface).IsInContact)
        {
            return;
        }

        var current = e.GetCurrentPoint(InteractionSurface).Position;
        await ViewModel.HandleDragAsync(
            current.X - _dragStartPoint.Value.X,
            current.Y - _dragStartPoint.Value.Y);
        _dragStartPoint = current;
    }

    private void OnVideoPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragStartPoint = null;
        RestartAutoHideTimer();
    }

    private async void OnTimelineManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        await ViewModel.SeekToAsync(slider.Value);
        RestartAutoHideTimer();
    }

    private void EnsureAutoHideTimer()
    {
        if (_autoHideTimer is not null)
        {
            return;
        }

        _autoHideTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _autoHideTimer.Interval = TimeSpan.FromSeconds(3);
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += OnAutoHideTimerTick;
    }

    private void RestartAutoHideTimer()
    {
        _autoHideTimer?.Stop();
        _autoHideTimer?.Start();
    }

    private void OnAutoHideTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ViewModel.OnIdleTimeout();
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using MpvShell.Player.LibMpv;
using Vortice.DXGI;
using PanelSwapChainNative = MpvShell.Rendering.WinUI.Interop.ISwapChainPanelNative;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// libmpv Render API → ANGLE/D3D11 → Composition SwapChainPanel 的 SDR 渲染器。
/// UI 线程仅操作面板；独立线程拥有全部图形资源及 render context。
/// </summary>
public sealed class D3D11VideoSurfaceRenderer : IVideoSurfaceRenderer
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _resizeGate = new();
    private RenderWorker? _worker;
    private MpvPlayerSession? _session;
    private SwapChainPanel? _surface;
    private DispatcherQueue? _dispatcher;
    private VideoSurfaceSize _requestedSize = new(1, 1, 1);
    private bool _disposed;
    private int _recoveryPending;
    private int _recoveryAttempts;

    // 以下资源/字段只由 RenderWorker 访问。
    private D3D11DeviceManager? _deviceManager;
    private CompositionSwapChain? _swapChain;
    private AngleContext? _angle;
    private MpvRenderContext? _renderContext;
    private VideoSurfaceSize _currentSize;
    private bool _canPresent;
    private bool _forceRedraw;
    private long _presentedFrames;

    /// <summary>可能从后台线程触发；订阅方自行通过 DispatcherQueue 更新 UI。</summary>
    public event Action<string>? RenderingFailed;
    public event Action<string>? DiagnosticMessage;

    public async ValueTask InitializeAsync(IMpvPlayerSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is not MpvPlayerSession realSession)
            throw new ArgumentException("视频渲染要求共享的真实 libmpv 会话。", nameof(session));

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is not null)
            {
                if (!ReferenceEquals(_session, realSession))
                    throw new InvalidOperationException("渲染器已绑定另一播放器会话。");
                return;
            }
            _session = realSession;
            _worker = new RenderWorker(RenderFrame, OnRenderFailure);
            try
            {
                await _worker.InvokeAsync(() => CreateGraphics(GetRequestedSize()), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await _worker.InvokeAsync(ReleaseGraphics).ConfigureAwait(false);
                await _worker.DisposeAsync().ConfigureAwait(false);
                _worker = null;
                _session = null;
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask AttachAsync(SwapChainPanel surface, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var dispatcher = surface.DispatcherQueue;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var worker = _worker ?? throw new InvalidOperationException("必须先初始化渲染器。");
            if (_surface is not null)
                await DetachSurfaceCoreAsync().ConfigureAwait(false);

            await OnUiAsync(dispatcher, () =>
            {
                lock (_resizeGate)
                    _requestedSize = new VideoSurfaceSize(surface.ActualWidth, surface.ActualHeight, surface.RasterizationScale);
            }).ConfigureAwait(false);

            nint swapChainPointer = 0;
            await worker.InvokeAsync(() =>
            {
                if (_swapChain is null) CreateGraphics(GetRequestedSize());
                else ResizeGraphics(GetRequestedSize());
                swapChainPointer = _swapChain!.NativePointer;
            }, cancellationToken).ConfigureAwait(false);

            await OnUiAsync(dispatcher, () => SetSwapChain(surface, swapChainPointer)).ConfigureAwait(false);
            _surface = surface;
            _dispatcher = dispatcher;
            await worker.InvokeAsync(() => { _canPresent = true; _forceRedraw = true; }).ConfigureAwait(false);
            Log("视频表面已绑定，开始等待 libmpv 帧更新。");
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask ResizeAsync(VideoSurfaceSize size, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_resizeGate) _requestedSize = size;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || _worker is null || _surface is null) return;
            // 队列项读取最新尺寸；连续 UI 请求不会重放每个中间尺寸。
            await _worker.InvokeAsync(() => ResizeGraphics(GetRequestedSize()), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"调整视频尺寸失败：{ex}");
            QueueRecovery(ex);
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DetachAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_worker is null) return;
            try { await DetachSurfaceCoreAsync().ConfigureAwait(false); }
            finally
            {
                // UI 调度器已经关闭也必须归还 mpv core 租约，避免后端退出等待不结束。
                await _worker.InvokeAsync(ReleaseGraphics).ConfigureAwait(false);
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            if (_worker is not null)
            {
                try { await DetachSurfaceCoreAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    // XAML 通过 COM 持有自己的 swapchain 引用；此处可以释放本方资源。
                    // 关闭不能因 UI 解绑失败遗留 render context，让后端永远等待租约。
                    Log($"关闭时解绑视频表面失败，继续释放播放器渲染资源：{ex}");
                }
                finally
                {
                    await _worker.InvokeAsync(ReleaseGraphics).ConfigureAwait(false);
                    await _worker.DisposeAsync().ConfigureAwait(false);
                    _worker = null;
                    _surface = null;
                    _dispatcher = null;
                }
            }
            _session = null;
            _disposed = true;
        }
        finally { _lifecycle.Release(); }
    }

    private async Task DetachSurfaceCoreAsync()
    {
        await _worker!.InvokeAsync(() => _canPresent = false).ConfigureAwait(false);
        if (_surface is not null)
        {
            var surface = _surface;
            await OnUiAsync(_dispatcher!, () => SetSwapChain(surface, 0)).ConfigureAwait(false);
            _surface = null;
            _dispatcher = null;
        }
    }

    private void CreateGraphics(VideoSurfaceSize size)
    {
        try
        {
            _deviceManager = new D3D11DeviceManager();
            _deviceManager.Initialize();
            _angle = new AngleContext(_deviceManager.GetDevicePointer());
            _swapChain = new CompositionSwapChain(_deviceManager.GetFactory(), _deviceManager.GetDevice(),
                size.PhysicalWidth, size.PhysicalHeight, Format.B8G8R8A8_UNorm);
            _swapChain.SetScale(size.RasterizationScale);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
            _renderContext = _session!.CreateRenderContext(_angle.GetProcAddress, _worker!.Wake);
            _currentSize = size;
            _forceRedraw = true;
            Log($"渲染初始化完成：{_angle.Description}；SDR BGRA8，{size.PhysicalWidth}×{size.PhysicalHeight}。");
        }
        catch
        {
            ReleaseGraphics();
            throw;
        }
    }

    private void ResizeGraphics(VideoSurfaceSize size)
    {
        if (_swapChain is null || _angle is null) return;
        if (_currentSize.PhysicalWidth != size.PhysicalWidth || _currentSize.PhysicalHeight != size.PhysicalHeight)
        {
            _angle.ReleaseBackBuffer();
            _swapChain.Resize(size.PhysicalWidth, size.PhysicalHeight);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
            _forceRedraw = true;
        }
        if (_currentSize.RasterizationScale != size.RasterizationScale)
            _swapChain.SetScale(size.RasterizationScale);
        _currentSize = size;
    }

    private void RenderFrame()
    {
        if (!_canPresent || _angle is null || _renderContext is null || _swapChain is null) return;
        _angle.MakeBackBufferCurrent();
        var hasFrame = _renderContext.Update();
        if (!hasFrame && !_forceRedraw) return;
        _forceRedraw = false;
        // ANGLE 的 D3D 纹理 pbuffer 已处理目标坐标方向；再次翻转会使实际视频倒置。
        _renderContext.Render(0, checked((int)_currentSize.PhysicalWidth), checked((int)_currentSize.PhysicalHeight), flipY: false);
        _angle.PreparePresent();
        _swapChain.Present();
        _renderContext.ReportSwap();
        _presentedFrames++;
        if (_presentedFrames == 1 || _presentedFrames % 300 == 0)
            Log($"已呈现 {_presentedFrames} 帧，{_currentSize.PhysicalWidth}×{_currentSize.PhysicalHeight}。");
    }

    private void ReleaseGraphics()
    {
        _canPresent = false;
        // mpv render context 必须在 EGL current 且 core 尚存活时先释放。
        // 即使设备已丢失，也尽力释放其余资源，保留最先发生的异常。
        Exception? failure = null;
        try { _angle?.MakeParkingCurrent(); } catch (Exception ex) { failure = ex; }
        try { _renderContext?.Dispose(); } catch (Exception ex) { failure ??= ex; }
        _renderContext = null;
        try { _angle?.Dispose(); } catch (Exception ex) { failure ??= ex; }
        _angle = null;
        try { _swapChain?.Dispose(); } catch (Exception ex) { failure ??= ex; }
        _swapChain = null;
        try { _deviceManager?.Dispose(); } catch (Exception ex) { failure ??= ex; }
        _deviceManager = null;
        if (failure is not null) Log($"图形资源清理遇到错误：{failure}");
        Log("渲染上下文、EGL、SwapChain 和 D3D11 资源已按顺序释放。");
    }

    private void OnRenderFailure(Exception exception)
    {
        _canPresent = false;
        QueueRecovery(exception);
    }

    private void QueueRecovery(Exception exception)
    {
        Log($"视频渲染失败：{exception}");
        if (Interlocked.CompareExchange(ref _recoveryPending, 1, 0) == 0)
            _ = Task.Run(() => RecoverGraphicsAsync(exception));
    }

    private async Task RecoverGraphicsAsync(Exception originalFailure)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || _worker is null || _surface is null) return;
            if (_recoveryAttempts++ >= 1)
            {
                ReportFailure($"视频渲染恢复后再次失败，请重新打开播放器。{originalFailure.Message}");
                return;
            }
            var surface = _surface;
            var dispatcher = _dispatcher!;
            await _worker.InvokeAsync(() => _canPresent = false).ConfigureAwait(false);
            await OnUiAsync(dispatcher, () => SetSwapChain(surface, 0)).ConfigureAwait(false);
            nint pointer = 0;
            await _worker.InvokeAsync(() =>
            {
                ReleaseGraphics();
                CreateGraphics(GetRequestedSize());
                pointer = _swapChain!.NativePointer;
            }).ConfigureAwait(false);
            await OnUiAsync(dispatcher, () => SetSwapChain(surface, pointer)).ConfigureAwait(false);
            await _worker.InvokeAsync(() => { _canPresent = true; _forceRedraw = true; }).ConfigureAwait(false);
            Log("视频渲染链已自动重建。");
        }
        catch (Exception ex)
        {
            Log($"自动重建视频渲染链失败：{ex}");
            ReportFailure($"视频渲染失败，自动重建未成功。{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _recoveryPending, 0);
            _lifecycle.Release();
        }
    }

    private VideoSurfaceSize GetRequestedSize()
    {
        lock (_resizeGate) return _requestedSize;
    }

    private static void SetSwapChain(SwapChainPanel surface, nint pointer)
    {
        if (!surface.DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("SwapChainPanel 只能在 UI 线程绑定。");
        var native = WinRT.CastExtensions.As<PanelSwapChainNative>(surface);
        Marshal.ThrowExceptionForHR(native.SetSwapChain(pointer));
    }

    private static Task OnUiAsync(DispatcherQueue dispatcher, Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Execute()
        {
            try { action(); completion.TrySetResult(); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }
        if (dispatcher.HasThreadAccess) Execute();
        else if (!dispatcher.TryEnqueue(Execute))
            completion.TrySetException(new InvalidOperationException("UI 调度器已关闭，无法更新视频表面。"));
        return completion.Task;
    }

    private void Log(string message)
    {
        Trace.WriteLine($"[D3D11VideoSurfaceRenderer] {message}");
        try { DiagnosticMessage?.Invoke(message); } catch { }
    }

    private void ReportFailure(string message)
    {
        try { RenderingFailed?.Invoke(message); } catch { }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using MpvShell.Player.LibMpv;
using Vortice.DXGI;
using PanelSwapChainNative = MpvShell.Rendering.WinUI.Interop.ISwapChainPanelNative;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// libmpv Render API → ANGLE/D3D11 → Composition SwapChainPanel 的 SDR/HDR10 渲染器。
/// UI 线程仅操作面板；独立线程拥有全部图形资源及 render context。
/// </summary>
public sealed partial class D3D11VideoSurfaceRenderer : IVideoSurfaceRenderer
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _resizeGate = new();
    private RenderWorker? _worker;
    private MpvPlayerSession? _session;
    private SwapChainPanel? _surface;
    private DispatcherQueue? _dispatcher;
    private VideoSurfaceSize _requestedSize = new(1, 1, 1);
    private bool _disposed;
    private bool _fatalFailure;
    private int _recoveryPending;
    private int _recoveryAttempts;
    private VideoOutputConfiguration _requestedOutput = VideoOutputConfiguration.Sdr;
    private VideoOutputConfiguration? _failedOutput;
    private VideoOutputConfiguration _activeOutput = VideoOutputConfiguration.Sdr;
    private DisplayOutputCapabilities? _displayCapabilities;
    private bool _isHdrSource;
    private string? _outputFallback;

    // 以下资源/字段只由 RenderWorker 访问。
    private D3D11DeviceManager? _deviceManager;
    private CompositionSwapChain? _swapChain;
    private AngleContext? _angle;
    private MpvRenderContext? _renderContext;
    private VideoSurfaceSize _currentSize;
    // _canPresent 只控制是否向 SwapChain 呈现；为 false 时仍以“跳过绘制”方式响应 mpv 的渲染请求，
    // 否则 core 每帧等待 200 ms 超时并计为 VO 丢帧。_renderSuspended 才完全停止调用 Render API，
    // 仅用于图形故障、恢复重建与终态。
    private bool _canPresent;
    private bool _renderSuspended;
    private bool _forceRedraw;
    private long _presentedFrames;
    private long _skippedFrames;
    private long _frameWakeTimestamp;
    private double _maximumRenderMilliseconds;
    private double _maximumPresentMilliseconds;

    /// <summary>可能从后台线程触发；订阅方自行通过 DispatcherQueue 更新 UI。</summary>
    public event Action<string>? RenderingFailed;
    public event Action<string>? DiagnosticMessage;
    public event Action<string>? OutputStatusChanged;

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
                await _session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Sdr, 203, cancellationToken).ConfigureAwait(false);
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
            if (_fatalFailure) throw new InvalidOperationException("视频渲染已停止，请重新打开播放器。");
            if (_surface is not null)
                await DetachSurfaceCoreAsync().ConfigureAwait(false);

            await OnUiAsync(dispatcher, () =>
            {
                // 元素级 RasterizationScale 默认恒为 1.0；显示器 DPI 缩放来自 XamlRoot，否则表面只有逻辑像素大小。
                lock (_resizeGate)
                    _requestedSize = new VideoSurfaceSize(surface.ActualWidth, surface.ActualHeight, surface.XamlRoot?.RasterizationScale ?? 1.0);
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
            PublishOutputStatus();
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>显示器状态来自 Windows，源动态范围来自后端强类型信息；切换期间停止呈现但继续响应 mpv 渲染请求。</summary>
    public async ValueTask UpdateOutputAsync(bool isHdrSource, DisplayOutputCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || _fatalFailure) return;
            _isHdrSource = isHdrSource;
            if (_displayCapabilities != capabilities) _failedOutput = null;
            _displayCapabilities = capabilities;
            _requestedOutput = VideoOutputConfiguration.Select(isHdrSource,
                capabilities.IsAvailable && capabilities.IsHdrEnabled, capabilities.PeakLuminanceInNits);
            if (_worker is null || _session is null) return;
            if (_requestedOutput != _activeOutput && _requestedOutput != _failedOutput)
            {
                try
                {
                    await ChangeOutputCoreAsync(_requestedOutput).ConfigureAwait(false);
                    _failedOutput = null;
                    _outputFallback = null;
                }
                catch (Exception ex) when (_requestedOutput.IsHdr)
                {
                    // 不能把 PQ 编码的帧交给 SDR SwapChain；回退时连同 mpv 的目标色彩一起恢复。
                    Log($"HDR10 输出初始化失败，切换至 SDR 色调映射：{ex}");
                    await ChangeOutputCoreAsync(VideoOutputConfiguration.Sdr).ConfigureAwait(false);
                    _failedOutput = _requestedOutput;
                    _outputFallback = "HDR 输出不可用，已转为 SDR";
                }
            }
            else if (_requestedOutput == _activeOutput)
            {
                _failedOutput = null;
                _outputFallback = null;
            }
            PublishOutputStatus();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            QueueRecovery(ex);
            // 可恢复故障由 RenderingFailed 只报告最终失败，避免页面留下已恢复的错误。
        }
        finally { _lifecycle.Release(); }
    }

    // 调用方持有生命周期锁；普通 mpv 属性只在命令域执行，不在渲染线程同步等待。
    // 期间只停止呈现，渲染线程继续以跳过绘制的方式消费帧，mpv 的色彩选项更新不会被 VO 等待拖慢。
    private async Task ChangeOutputCoreAsync(VideoOutputConfiguration output)
    {
        long skippedBefore = 0;
        await _worker!.InvokeAsync(() => { _canPresent = false; skippedBefore = _skippedFrames; }).ConfigureAwait(false);
        if (_surface is not null)
            await OnUiAsync(_dispatcher!, () => SetSwapChain(_surface, 0)).ConfigureAwait(false);
        await _session!.ConfigureVideoOutputAsync(output.Mode, output.PeakLuminance, CancellationToken.None).ConfigureAwait(false);
        nint pointer = 0;
        long skippedDuringChange = 0;
        await _worker.InvokeAsync(() =>
        {
            _angle!.ReleaseBackBuffer();
            _swapChain?.Dispose();
            _swapChain = null;
            var size = GetRequestedSize();
            _swapChain = new CompositionSwapChain(_deviceManager!.GetFactory(), _deviceManager.GetDevice(),
                size.PhysicalWidth, size.PhysicalHeight, output.Format);
            _swapChain.SetColorSpace(output.ColorSpace);
            _swapChain.SetScale(size.RasterizationScale);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
            _currentSize = size;
            _activeOutput = output;
            pointer = _swapChain.NativePointer;
            skippedDuringChange = _skippedFrames - skippedBefore;
        }).ConfigureAwait(false);
        if (_surface is not null)
            await OnUiAsync(_dispatcher!, () => SetSwapChain(_surface, pointer)).ConfigureAwait(false);
        await _worker.InvokeAsync(() => { _canPresent = _surface is not null; _forceRedraw = true; }).ConfigureAwait(false);
        Log($"输出已重配：{output.Format} / {output.ColorSpace}，目标峰值 {output.PeakLuminance:0} nit；保留当前媒体与渲染上下文；重配期间跳过呈现 {skippedDuringChange} 帧。");
    }

    private void PublishOutputStatus()
    {
        var display = _displayCapabilities;
        var mode = _activeOutput.IsHdr ? $"HDR10 · 10-bit PQ / BT.2020 · 目标 {_activeOutput.PeakLuminance:0} nit"
            : _isHdrSource ? "SDR · HDR 色调映射" : "SDR · 8-bit";
        var system = display?.IsAvailable != true ? "显示器状态未知"
            : display.IsHdrEnabled ? "Windows HDR 已开启"
            : display.IsHdrSupported ? "Windows HDR 未开启" : "当前显示器为 SDR";
        var summary = $"输出：{mode}；{system}";
        if (_activeOutput.IsHdr && display?.PeakLuminanceInNits is null)
            summary += "；目标峰值采用默认值";
        if (_outputFallback is not null) summary += $"；{_outputFallback}";
        Log($"{summary}；显示器 {display?.DisplayName ?? "未知"}。");
        try { OutputStatusChanged?.Invoke(summary); } catch { }
    }

    public async ValueTask ResizeAsync(VideoSurfaceSize size, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_resizeGate) _requestedSize = size;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || _fatalFailure || _worker is null || _surface is null) return;
            // 队列项读取最新尺寸；连续 UI 请求不会重放每个中间尺寸。
            await _worker.InvokeAsync(() => ResizeGraphics(GetRequestedSize()), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"调整视频尺寸失败：{ex}");
            QueueRecovery(ex);
            // 同步请求已交给恢复流程；只有恢复失败才向页面报告错误。
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
#if DEBUG
            _testBeforeCreate?.Invoke();
#endif
            _deviceManager = new D3D11DeviceManager();
            _deviceManager.Initialize();
            _angle = new AngleContext(_deviceManager.GetDevicePointer());
            _swapChain = new CompositionSwapChain(_deviceManager.GetFactory(), _deviceManager.GetDevice(),
                size.PhysicalWidth, size.PhysicalHeight, _activeOutput.Format);
            _swapChain.SetColorSpace(_activeOutput.ColorSpace);
            _swapChain.SetScale(size.RasterizationScale);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
            _renderContext = _session!.CreateRenderContext(_angle.GetProcAddress, () =>
            {
                Interlocked.CompareExchange(ref _frameWakeTimestamp, Stopwatch.GetTimestamp(), 0);
                _worker!.Wake();
            });
            _currentSize = size;
            _forceRedraw = true;
            _renderSuspended = false;
#if DEBUG
            _testGraphicsGeneration++;
#endif
            Log($"渲染初始化完成：{_angle.Description}；{_activeOutput.Format}，{size.PhysicalWidth}×{size.PhysicalHeight}。");
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
#if DEBUG
            _testBeforeResize?.Invoke(_testGraphicsGeneration);
#endif
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
        if (_renderSuspended || _angle is null || _renderContext is null) return;
        if (!_canPresent || _swapChain is null)
        {
            ConsumeFrameWithoutPresenting();
            return;
        }
#if DEBUG
        _testBeforeRender?.Invoke(_testGraphicsGeneration);
#endif
        _angle.MakeBackBufferCurrent();
        var hasFrame = _renderContext.Update();
        if (!hasFrame && !_forceRedraw)
        {
            Interlocked.Exchange(ref _frameWakeTimestamp, 0);
            return;
        }
        _forceRedraw = false;
        var wakeTimestamp = Interlocked.Exchange(ref _frameWakeTimestamp, 0);
        var renderStart = Stopwatch.GetTimestamp();
        // ANGLE 的 D3D 纹理 pbuffer 已处理目标坐标方向；再次翻转会使实际视频倒置。
        _renderContext.Render(0, checked((int)_currentSize.PhysicalWidth), checked((int)_currentSize.PhysicalHeight),
            flipY: false, internalFormat: _angle.GlInternalFormat, depth: _angle.ColorDepth);
        _angle.PreparePresent();
#if DEBUG
        _testReadFrame?.Invoke(_deviceManager!, _swapChain, _testGraphicsGeneration);
#endif
        var presentStart = Stopwatch.GetTimestamp();
        _swapChain.Present();
        var presentEnd = Stopwatch.GetTimestamp();
        _renderContext.ReportSwap();
#if DEBUG
        _testPresented?.Invoke();
#endif
        _maximumRenderMilliseconds = Math.Max(_maximumRenderMilliseconds, Stopwatch.GetElapsedTime(renderStart, presentStart).TotalMilliseconds);
        _maximumPresentMilliseconds = Math.Max(_maximumPresentMilliseconds, Stopwatch.GetElapsedTime(presentStart, presentEnd).TotalMilliseconds);
        _presentedFrames++;
        if (_presentedFrames == 1 || _presentedFrames % 300 == 0)
        {
            var delay = wakeTimestamp > 0 ? Stopwatch.GetElapsedTime(wakeTimestamp, presentEnd).TotalMilliseconds : 0;
            Log($"已呈现 {_presentedFrames} 帧，{_currentSize.PhysicalWidth}×{_currentSize.PhysicalHeight}；" +
                $"{_activeOutput.Format}；本段最大 Render {_maximumRenderMilliseconds:0.00} ms / Present {_maximumPresentMilliseconds:0.00} ms；最近回调至呈现 {delay:0.00} ms；累计跳过呈现 {_skippedFrames} 帧。");
            _maximumRenderMilliseconds = _maximumPresentMilliseconds = 0;
        }
    }

    /// <summary>
    /// 表面未绑定或输出重配期间仍响应 mpv 的渲染请求：帧按正常时序被消费但不绘制、不呈现，
    /// core 不再等待超时或丢帧，PQ 帧也不会被画到旧的 SDR SwapChain。恢复呈现后由 _forceRedraw 补画当前帧。
    /// </summary>
    private void ConsumeFrameWithoutPresenting()
    {
        Interlocked.Exchange(ref _frameWakeTimestamp, 0);
        // 跳过绘制仍可能触发 mpv 的 reconfig/reset 等 GL 调用；后备缓冲可能已释放，使用 parking surface。
        _angle!.MakeParkingCurrent();
        if (!_renderContext!.Update()) return;
        _renderContext.Skip();
        _renderContext.ReportSwap();
        _skippedFrames++;
        _forceRedraw = true;
    }

    private void ReleaseGraphics()
    {
        _canPresent = false;
        _renderSuspended = true;
        Interlocked.Exchange(ref _frameWakeTimestamp, 0);
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
        // 故障后的渲染上下文/EGL 状态不可信，恢复重建之前完全停止调用 Render API。
        _canPresent = false;
        _renderSuspended = true;
        QueueRecovery(exception);
    }

    private void QueueRecovery(Exception exception)
    {
        if (Volatile.Read(ref _fatalFailure)) return;
        Log($"视频渲染失败：{exception}");
        if (Interlocked.CompareExchange(ref _recoveryPending, 1, 0) == 0)
            _ = Task.Run(() => RecoverGraphicsAsync(exception));
    }

    private async Task RecoverGraphicsAsync(Exception originalFailure)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        var ownsPendingFlag = true;
        try
        {
            if (_disposed || _fatalFailure || _worker is null || _surface is null) return;
            await _worker.InvokeAsync(() => { _canPresent = false; _renderSuspended = true; }).ConfigureAwait(false);
            if (_recoveryAttempts++ >= 1)
            {
                await EnterFatalFailureAsync($"视频渲染恢复后再次失败，请重新打开播放器。{originalFailure.Message}").ConfigureAwait(false);
                return;
            }
            var surface = _surface;
            var dispatcher = _dispatcher!;
            await OnUiAsync(dispatcher, () => SetSwapChain(surface, 0)).ConfigureAwait(false);
            var videoState = await _session!.SuspendVideoForRecoveryAsync(CancellationToken.None).ConfigureAwait(false);
            // 恢复到最新显示器要求，避免关 HDR 后因一次设备异常永久回到旧 PQ 输出。
            var output = _requestedOutput == _failedOutput ? VideoOutputConfiguration.Sdr : _requestedOutput;
            try
            {
                try
                {
                    await RebuildGraphicsCoreAsync(output, surface, dispatcher).ConfigureAwait(false);
                    if (output == _requestedOutput)
                    {
                        _failedOutput = null;
                        _outputFallback = null;
                    }
                }
                catch (Exception ex) when (output.IsHdr)
                {
                    Log($"重建 HDR 输出失败，尝试 SDR 色调映射：{ex}");
                    await RebuildGraphicsCoreAsync(VideoOutputConfiguration.Sdr, surface, dispatcher).ConfigureAwait(false);
                    _failedOutput = output;
                    _outputFallback = "HDR 输出不可用，已转为 SDR";
                }
                await _session.ResumeVideoAfterRecoveryAsync(videoState, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await _session.AbortVideoRecoveryAsync(videoState).ConfigureAwait(false);
            }
            // 在恢复呈现之前交还故障通知权；第一帧可以立即失败并排队下一次处理。
            // finally 不再清除此标记，否则会覆盖那一次新故障的 pending 状态。
            await _worker.InvokeAsync(() =>
            {
                ownsPendingFlag = false;
                Interlocked.Exchange(ref _recoveryPending, 0);
                _canPresent = true;
                _forceRedraw = true;
            }).ConfigureAwait(false);
            PublishOutputStatus();
            Log("视频渲染链已自动重建。");
        }
        catch (Exception ex)
        {
            Log($"自动重建视频渲染链失败：{ex}");
            await EnterFatalFailureAsync($"视频渲染失败，自动重建未成功，请重新打开播放器。{ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            if (ownsPendingFlag) Interlocked.Exchange(ref _recoveryPending, 0);
            _lifecycle.Release();
        }
    }

    // 调用方持有生命周期锁。终态同时停止音频时钟和视频；显示器变化不能重新启用它。
    private async Task EnterFatalFailureAsync(string message)
    {
        Volatile.Write(ref _fatalFailure, true);
        if (_worker is not null)
            await _worker.InvokeAsync(() => { _canPresent = false; _renderSuspended = true; }).ConfigureAwait(false);
        try
        {
            if (_session is not null)
                await _session.PauseForRenderingFailureAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"渲染终止后暂停媒体失败：{ex}");
            message += $" 暂停媒体失败：{ex.Message}";
        }
        ReportFailure(message);
    }

    private async Task RebuildGraphicsCoreAsync(VideoOutputConfiguration output, SwapChainPanel surface,
        DispatcherQueue dispatcher)
    {
        await _session!.ConfigureVideoOutputAsync(output.Mode, output.PeakLuminance,
            CancellationToken.None).ConfigureAwait(false);
        nint pointer = 0;
        await _worker!.InvokeAsync(() =>
        {
            ReleaseGraphics();
            _activeOutput = output;
            CreateGraphics(GetRequestedSize());
            pointer = _swapChain!.NativePointer;
        }).ConfigureAwait(false);
        await OnUiAsync(dispatcher, () => SetSwapChain(surface, pointer)).ConfigureAwait(false);
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

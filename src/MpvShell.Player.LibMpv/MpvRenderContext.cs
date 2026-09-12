using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Concurrent;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv;

/// <summary>渲染线程独占的 Render API 包装，持有 mpv core 租约且不暴露原生句柄。</summary>
public sealed class MpvRenderContext : IDisposable
{
    // 若原生 Free 入口发生托管互操作异常，保持回调/core 有效直到进程退出，禁止悬空函数指针。
    private static readonly ConcurrentBag<MpvRenderContext> FailedReleases = [];
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly MpvGetProcAddressCallback _getProcAddress;
    private readonly MpvWakeupCallback _updateCallback;
    private readonly Action _releaseSession;
    private readonly Action<Exception> _failSession;
    private nint _context;
    private bool _rendered;
    private Exception? _releaseFailure;

    internal unsafe MpvRenderContext(nint session, Func<string, nint> getProcAddress, Action updateCallback,
        Action releaseSession, Action<Exception> failSession)
    {
        _releaseSession = releaseSession;
        _failSession = failSession;
        _getProcAddress = (_, name) =>
        {
            try { return getProcAddress(Marshal.PtrToStringUTF8(name) ?? ""); }
            catch { return 0; }
        };
        _updateCallback = _ =>
        {
            try { updateCallback(); }
            catch { /* 托管异常不能穿过 libmpv 的更新回调。 */ }
        };

        var api = Marshal.StringToCoTaskMemUTF8("opengl");
        try
        {
            var init = new MpvOpenGlInitParameters { GetProcAddress = Marshal.GetFunctionPointerForDelegate(_getProcAddress) };
            // 采用非 advanced 模式；所有渲染调用仍严格限制在独立渲染线程。
            var parameters = stackalloc MpvRenderParameter[3];
            parameters[0] = new() { Type = 1, Data = api };
            parameters[1] = new() { Type = 2, Data = (nint)(&init) };
            parameters[2] = default;
            MpvNative.Check(MpvNative.RenderContextCreate(out _context, session, (nint)parameters), "创建 libmpv OpenGL 渲染上下文");
            MpvNative.RenderContextSetUpdateCallback(_context, Marshal.GetFunctionPointerForDelegate(_updateCallback), 0);
            Trace.WriteLine("[libmpv] OpenGL 渲染上下文创建完成。");
        }
        catch (Exception creationFailure)
        {
            if (_context != 0)
            {
                try { ReleaseNativeContext(); }
                catch (Exception releaseFailure)
                {
                    throw new AggregateException("创建渲染上下文失败，清理过程中也发生错误。", creationFailure, releaseFailure);
                }
            }
            throw;
        }
        finally { Marshal.FreeCoTaskMem(api); }
    }

    public bool Update()
    {
        EnsureThread();
        return (MpvNative.RenderContextUpdate(_context) & 1) != 0;
    }

    public unsafe void Render(int framebuffer, int width, int height, bool flipY = false, int internalFormat = 0, int depth = 8)
    {
        EnsureThread();
        ArgumentOutOfRangeException.ThrowIfNegative(framebuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(internalFormat);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        var target = new MpvOpenGlFramebuffer { Framebuffer = framebuffer, Width = width, Height = height, InternalFormat = internalFormat };
        var flip = flipY ? 1 : 0;
        var blockForTargetTime = 0; // video-timing-offset=0；呈现时机由渲染循环和 vsync 管理。
        var parameters = stackalloc MpvRenderParameter[5];
        parameters[0] = new() { Type = 3, Data = (nint)(&target) };
        parameters[1] = new() { Type = 4, Data = (nint)(&flip) };
        parameters[2] = new() { Type = 12, Data = (nint)(&blockForTargetTime) };
        parameters[3] = new() { Type = 5, Data = (nint)(&depth) }; // MPV_RENDER_PARAM_DEPTH，控制输出抖动精度。
        parameters[4] = default;
        MpvNative.Check(MpvNative.RenderContextRender(_context, (nint)parameters), "渲染视频帧");
        if (!_rendered)
        {
            _rendered = true;
            Trace.WriteLine($"[libmpv] 首次 Render API 提交：{width} × {height}。");
        }
    }

    /// <summary>
    /// 不绘制但按 mpv 的约定消费当前帧。呈现被暂停期间仍必须调用，否则 core 的 flip_page
    /// 会等待 200 ms 超时并把该帧记为 VO 丢帧。调用后仍需 <see cref="ReportSwap"/> 保持计数一致。
    /// </summary>
    public unsafe void Skip()
    {
        EnsureThread();
        var skip = 1;
        var blockForTargetTime = 0;
        var parameters = stackalloc MpvRenderParameter[3];
        parameters[0] = new() { Type = 13, Data = (nint)(&skip) }; // MPV_RENDER_PARAM_SKIP_RENDERING
        parameters[1] = new() { Type = 12, Data = (nint)(&blockForTargetTime) };
        parameters[2] = default;
        MpvNative.Check(MpvNative.RenderContextRender(_context, (nint)parameters), "跳过视频帧渲染");
    }

    public void ReportSwap()
    {
        EnsureThread();
        MpvNative.RenderContextReportSwap(_context);
    }

    private void EnsureThread()
    {
        ObjectDisposedException.ThrowIf(_context == 0, this);
        if (_threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("libmpv Render API 必须在创建它的渲染线程调用。");
    }

    public void Dispose()
    {
        if (_releaseFailure is not null) throw new InvalidOperationException("渲染上下文此前释放失败。", _releaseFailure);
        if (_context == 0) return;
        EnsureThread();
        ReleaseNativeContext();
    }

    private void ReleaseNativeContext()
    {
        Exception? unregisterFailure;
        try
        {
            unregisterFailure = ReleaseNativeResources(
                () => MpvNative.RenderContextSetUpdateCallback(_context, 0, 0),
                () => MpvNative.RenderContextFree(_context));
        }
        catch (Exception ex)
        {
            _releaseFailure = new InvalidOperationException("无法释放原生渲染上下文；相关资源将保留至进程退出。", ex);
            FailedReleases.Add(this);
            _failSession(_releaseFailure);
            throw _releaseFailure;
        }
        _context = 0;
        GC.KeepAlive(_getProcAddress);
        GC.KeepAlive(_updateCallback);
        _releaseSession();
        Trace.WriteLine("[libmpv] 渲染上下文已释放。");
        if (unregisterFailure is not null)
            throw new InvalidOperationException("注销渲染回调失败，但原生上下文已完成释放。", unregisterFailure);
    }

    internal static Exception? ReleaseNativeResources(Action unregister, Action free)
    {
        Exception? unregisterFailure = null;
        try { unregister(); }
        catch (Exception ex) { unregisterFailure = ex; }

        try { free(); }
        catch (Exception ex)
        {
            if (unregisterFailure is not null)
                throw new AggregateException("注销渲染回调及释放原生上下文均失败。", unregisterFailure, ex);
            throw;
        }
        return unregisterFailure;
    }
}

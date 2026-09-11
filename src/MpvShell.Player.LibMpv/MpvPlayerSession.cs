using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv;

/// <summary>一份 mpv core 的明确所有权对象；普通 Client API 只在事件/命令线程执行。</summary>
public sealed partial class MpvPlayerSession : IMpvPlayerSession
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly string[] ObservedProperties =
    [
        "pause", "time-pos", "duration", "volume", "mute", "idle-active", "eof-reached",
        "paused-for-cache", "track-list", "video-params", "video-codec", "audio-codec-name",
        "estimated-vf-fps", "container-fps", "cache-buffering-state", "hwdec-current",
        "hwdec-interop", "decoder-frame-drop-count", "frame-drop-count",
    ];

    private readonly object _lifecycle = new();
    private readonly SemaphoreSlim _videoOutputGate = new(1, 1);
    private readonly AutoResetEvent _eventSignal = new(false);
    private readonly ConcurrentQueue<Action<nint>> _commands = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<object?>> _pending = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MpvWakeupCallback _wakeupCallback;
    private readonly IReadOnlyDictionary<string, string>? _testOptions;
    private readonly string _logLevel;
    private TaskCompletionSource? _renderReleased;
    private Task? _disposeTask;
    private nint _handle;
    private long _nextRequestId;
    private bool _started;
    private bool _disposing;
    private bool _faulted;
    private volatile bool _stop;

    internal event Action<SessionEvent>? EventReceived;

    public MpvPlayerSession() : this(null) { }

    internal MpvPlayerSession(IReadOnlyDictionary<string, string>? testOptions, string logLevel = "error")
    {
        _testOptions = testOptions;
        _logLevel = logLevel;
        _wakeupCallback = _ =>
        {
            // 回调不解析事件、不调用 Client API，不允许托管异常越过 C ABI。
            try { _eventSignal.Set(); }
            catch (ObjectDisposedException) { }
        };
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (!_started)
            {
                _started = true;
                new Thread(Run) { IsBackground = true, Name = "MpvShell.LibMpv" }.Start();
            }
        }
        return new ValueTask(_ready.Task.WaitAsync(RequestTimeout, cancellationToken));
    }

    /// <summary>必须由已激活 OpenGL 上下文的渲染线程调用；同一 core 同时只允许一个渲染上下文。</summary>
    public MpvRenderContext CreateRenderContext(Func<string, nint> getProcAddress, Action updateCallback)
    {
        ArgumentNullException.ThrowIfNull(getProcAddress);
        ArgumentNullException.ThrowIfNull(updateCallback);
        nint handle;
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (!_ready.Task.IsCompletedSuccessfully || _faulted)
                throw new InvalidOperationException("必须先初始化 mpv 会话。");
            if (_renderReleased is not null)
                throw new InvalidOperationException("同一 mpv 会话不能同时创建两个渲染上下文。");
            _renderReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
            handle = _handle;
        }

        // 这里不持有 Client API 线程需要的锁。渲染上下文归还租约前，DisposeAsync 不销毁 core。
        try { return new MpvRenderContext(handle, getProcAddress, updateCallback, ReleaseRenderContext, FailRenderContext); }
        catch { ReleaseRenderContext(); throw; }
    }

    private void ReleaseRenderContext()
    {
        lock (_lifecycle)
        {
            if (_renderReleased?.Task.IsFaulted is true) return;
            _renderReleased?.TrySetResult();
            _renderReleased = null;
        }
    }

    private void FailRenderContext(Exception error)
    {
        lock (_lifecycle)
        {
            _faulted = true;
            _renderReleased?.TrySetException(error);
        }
        Trace.WriteLine($"[libmpv] 渲染资源释放失败，保留 core 至进程结束：{error.Message}");
    }

    internal Task<object?> CommandAsync(string[] arguments, CancellationToken cancellationToken) =>
        MutatePlaybackAsync(() => CommandCoreAsync(arguments, cancellationToken), cancellationToken);

    private Task<object?> CommandCoreAsync(string[] arguments, CancellationToken cancellationToken) =>
        RequestAsync((handle, id) =>
        {
            using var utf8 = new Utf8Arguments(arguments);
            return MpvNative.CommandAsync(handle, id, utf8.Pointer);
        }, cancellationToken);

    internal Task<object?> GetPropertyAsync(string name, CancellationToken cancellationToken) =>
        RequestAsync((handle, id) => MpvNative.GetPropertyAsync(handle, id, name, MpvFormat.Node), cancellationToken);

    internal Task<object?> SetPropertyAsync(string name, object value, CancellationToken cancellationToken) =>
        MutatePlaybackAsync(() => SetPropertyCoreAsync(name, value, cancellationToken), cancellationToken,
            allowAfterRenderingFailure: name is "volume" or "mute" || name == "pause" && value is true);

    private Task<object?> SetPropertyCoreAsync(string name, object value, CancellationToken cancellationToken) =>
        RequestAsync((handle, id) => SetProperty(handle, id, name, value), cancellationToken);

    /// <summary>调用方先暂停呈现，再异步设置色彩目标；全部普通 C API 仍由事件/命令线程执行。</summary>
    public async ValueTask ConfigureVideoOutputAsync(MpvVideoOutputMode mode, double peakLuminance, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!double.IsFinite(peakLuminance) || peakLuminance <= 0)
            throw new ArgumentOutOfRangeException(nameof(peakLuminance), "目标峰值亮度必须为有限正数。");
        await _videoOutputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var hdr = mode == MpvVideoOutputMode.Hdr10;
            // 图形恢复期间持有播放修改锁；色彩配置由渲染器生命周期锁与本方法的锁串行化。
            await SetPropertyCoreAsync("options/target-prim", hdr ? "bt.2020" : "bt.709", cancellationToken).ConfigureAwait(false);
            await SetPropertyCoreAsync("options/target-trc", hdr ? "pq" : "srgb", cancellationToken).ConfigureAwait(false);
            // 上游 target-peak 是带 auto 枚举项的整数选项，不接受 MPV_FORMAT_DOUBLE。
            var peak = hdr ? (int)Math.Round(Math.Clamp(peakLuminance, 203, 10000)) : 203;
            await SetPropertyCoreAsync("options/target-peak", peak.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await SetPropertyCoreAsync("options/tone-mapping", "mobius", cancellationToken).ConfigureAwait(false);
            // GLES 3.0 不保证 compute shader；使用片源峰值元数据，避免依赖峰值检测计算着色器。
            await SetPropertyCoreAsync("options/hdr-compute-peak", "no", cancellationToken).ConfigureAwait(false);
        }
        finally { _videoOutputGate.Release(); }
    }

    private static unsafe int SetProperty(nint handle, ulong id, string name, object value)
    {
        switch (value)
        {
            case bool flag:
                var nativeFlag = flag ? 1 : 0;
                return MpvNative.SetPropertyAsync(handle, id, name, MpvFormat.Flag, (nint)(&nativeFlag));
            case int integer:
                long nativeInteger = integer;
                return MpvNative.SetPropertyAsync(handle, id, name, MpvFormat.Int64, (nint)(&nativeInteger));
            case double number:
                return MpvNative.SetPropertyAsync(handle, id, name, MpvFormat.Double, (nint)(&number));
            case string text:
                var utf8 = Marshal.StringToCoTaskMemUTF8(text);
                try { return MpvNative.SetPropertyAsync(handle, id, name, MpvFormat.String, (nint)(&utf8)); }
                finally { Marshal.FreeCoTaskMem(utf8); }
            default:
                throw new ArgumentException("不支持的属性值类型。", nameof(value));
        }
    }

    private async Task<object?> RequestAsync(Func<nint, ulong, int> send, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = (ulong)Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (!_ready.Task.IsCompletedSuccessfully || _stopped.Task.IsCompleted || _faulted)
                throw new InvalidOperationException("mpv 会话尚未初始化或已经停止。");
            _pending[id] = completion;
            _commands.Enqueue(handle =>
            {
                // 排队期间取消的操作无需再进入 mpv；已提交的操作仍允许自然完成。
                if (!_pending.ContainsKey(id)) return;
                try { MpvNative.Check(send(handle, id), "播放器请求"); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            _eventSignal.Set();
        }

        try { return await completion.Task.WaitAsync(RequestTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException ex) { throw new TimeoutException("播放器请求超时，请检查媒体或重新加载。", ex); }
        finally { _pending.TryRemove(id, out _); }
    }

    private void Run()
    {
        nint handle = 0;
        try
        {
            handle = MpvNative.Create();
            if (handle == 0)
                throw new InvalidOperationException("无法创建 libmpv 播放会话。");

            foreach (var (name, value) in InitializationOptions())
            {
                var result = MpvNative.SetOptionString(handle, name, value);
                // 锁定产物编译时禁用 Lua/JavaScript；相应脚本选项可能不存在。
                if (result == -5 && name is "load-scripts" or "osc" or "ytdl") continue;
                MpvNative.Check(result, $"设置播放器选项 {name}");
            }

            MpvNative.Check(MpvNative.Initialize(handle), "初始化 libmpv");
            MpvNative.SetWakeupCallback(handle, Marshal.GetFunctionPointerForDelegate(_wakeupCallback), 0);
            MpvNative.Check(MpvNative.RequestLogMessages(handle, _logLevel), "订阅播放器日志");
            ulong observation = 1;
            foreach (var property in ObservedProperties)
                MpvNative.Check(MpvNative.ObserveProperty(handle, observation++, property, MpvFormat.Node), $"观察属性 {property}");

            lock (_lifecycle) { _handle = handle; }
            Trace.WriteLine("[libmpv] 会话初始化完成，Client API 2.5，Render API 路线。");
            _ready.TrySetResult();

            while (!_stop)
            {
                // 对请求和事件轮流设置批量上限，避免连续输入饿死事件回复。
                for (var count = 0; count < 64 && _commands.TryDequeue(out var command); count++)
                    command(handle);

                var exhausted = false;
                for (var count = 0; count < 256 && !_stop; count++)
                {
                    var nativeEvent = Marshal.PtrToStructure<MpvEvent>(MpvNative.WaitEvent(handle, 0));
                    if (nativeEvent.Id == MpvEventId.None) { exhausted = true; break; }
                    try { Dispatch(nativeEvent); }
                    catch (Exception ex) { Publish(new SessionEvent(nativeEvent.Id, Error: ex)); }
                }

                if (!_stop && exhausted && _commands.IsEmpty)
                    _eventSignal.WaitOne();
            }
        }
        catch (Exception ex)
        {
            lock (_lifecycle) _faulted = true;
            _ready.TrySetException(ex);
            Publish(new SessionEvent(MpvEventId.Shutdown, Error: ex));
        }
        finally
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(new ObjectDisposedException(nameof(MpvPlayerSession), "播放器会话已关闭。"));
            Task renderReleased;
            lock (_lifecycle) renderReleased = _renderReleased?.Task ?? Task.CompletedTask;
            // 异常退出也遵守 context → core 的释放顺序。此处不持有任何原生锁；
            // 渲染线程从不等待本线程，仍可独立调用 Render API 完成释放。
            var canDestroy = true;
            try { renderReleased.GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                // context 未能释放时不能销毁仍被它使用的 core；调用方会收到明确错误。
                canDestroy = false;
                _stopped.TrySetException(ex);
            }
            if (handle != 0 && canDestroy)
            {
                MpvNative.SetWakeupCallback(handle, 0, 0);
                MpvNative.TerminateDestroy(handle);
            }
            GC.KeepAlive(_wakeupCallback);
            if (canDestroy)
            {
                lock (_lifecycle) _handle = 0;
                Trace.WriteLine("[libmpv] 会话已销毁。");
            }
            _stopped.TrySetResult();
        }
    }

    private IEnumerable<KeyValuePair<string, string>> InitializationOptions()
    {
        var options = new Dictionary<string, string>
        {
            ["config"] = "no", ["load-scripts"] = "no", ["osc"] = "no", ["ytdl"] = "no",
            ["input-default-bindings"] = "no", ["input-vo-keyboard"] = "no", ["terminal"] = "no",
            ["vo"] = "libmpv", ["idle"] = "yes", ["osd-level"] = "0",
            // 固定使用 ANGLE 的 D3D11 解码纹理互操作；不支持时由 mpv 回退软件解码，禁止 copy-back。
            ["hwdec"] = "d3d11va", ["gpu-hwdec-interop"] = "d3d11-egl", ["hwdec-software-fallback"] = "3",
            ["target-prim"] = "bt.709", ["target-trc"] = "srgb", ["target-peak"] = "203",
            ["tone-mapping"] = "mobius", ["hdr-compute-peak"] = "no",
            ["network-timeout"] = "10", ["video-timing-offset"] = "0",
            // MPEG-TS demux seek 可能落在目标之后；预留解码区间，保证重建后的 exact seek
            // 能重新解码原暂停帧。固定配置避免命令回复早于 seek 执行时恢复临时选项的竞态。
            ["hr-seek-demuxer-offset"] = "1",
        };
        if (_testOptions is not null)
            foreach (var option in _testOptions) options[option.Key] = option.Value;
        return options;
    }

    private void Dispatch(MpvEvent nativeEvent)
    {
        if (nativeEvent.Id is MpvEventId.CommandReply or MpvEventId.GetPropertyReply or MpvEventId.SetPropertyReply)
        {
            if (!_pending.TryGetValue(nativeEvent.ReplyUserData, out var pending)) return;
            if (nativeEvent.Error < 0)
                pending.TrySetException(new MpvException("播放器请求", nativeEvent.Error));
            else
                pending.TrySetResult(nativeEvent.Id == MpvEventId.GetPropertyReply
                    ? MpvNodeReader.ReadProperty(Marshal.PtrToStructure<MpvEventProperty>(nativeEvent.Data)) : null);
            return;
        }

        if (nativeEvent.Id == MpvEventId.PropertyChange)
        {
            var property = Marshal.PtrToStructure<MpvEventProperty>(nativeEvent.Data);
            Publish(new SessionEvent(nativeEvent.Id, Marshal.PtrToStringUTF8(property.Name), MpvNodeReader.ReadProperty(property)));
        }
        else if (nativeEvent.Id == MpvEventId.EndFile)
        {
            var end = Marshal.PtrToStructure<MpvEndFile>(nativeEvent.Data);
            Publish(new SessionEvent(nativeEvent.Id, Value: end.Reason,
                Error: end.Reason == 4 ? new MpvException("加载或播放媒体", end.Error) : null));
        }
        else if (nativeEvent.Id == MpvEventId.LogMessage)
        {
            // mpv_event_log_message 的第三个字段为 UTF-8 text，不记录含令牌的原始 URL。
            var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(nativeEvent.Data, 2 * IntPtr.Size)) ?? "";
            Trace.WriteLine("[libmpv] " + Regex.Replace(message, @"https?://[^\s]+", "[媒体地址]", RegexOptions.IgnoreCase));
        }
        else if (nativeEvent.Id == MpvEventId.QueueOverflow)
            Publish(new SessionEvent(nativeEvent.Id, Error: new InvalidOperationException("播放器事件队列溢出，请重新加载媒体。")));
        else
            Publish(new SessionEvent(nativeEvent.Id));
    }

    private void Publish(SessionEvent playerEvent)
    {
        // 订阅者错误不能破坏原生资源释放顺序，也不能结束事件队列的排空。
        try { EventReceived?.Invoke(playerEvent); }
        catch (Exception ex) { Trace.WriteLine($"播放器事件订阅处理失败：{ex.GetType().Name}"); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposing = true;
            _disposeTask = DisposeCoreAsync(_renderReleased?.Task ?? Task.CompletedTask);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(Task renderReleased)
    {
        // 异步等待渲染线程先释放上下文；该等待不占据事件线程，也不持有原生锁。
        await renderReleased.ConfigureAwait(false);
        _stop = true;
        _eventSignal.Set();
        if (_started)
            await _stopped.Task.ConfigureAwait(false);
        else
            _ready.TrySetException(new ObjectDisposedException(nameof(MpvPlayerSession)));
        _eventSignal.Dispose();
    }
}

internal sealed record SessionEvent(MpvEventId Id, string? Property = null, object? Value = null, Exception? Error = null);

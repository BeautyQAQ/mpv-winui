using System.Collections.Concurrent;

namespace MpvShell.Rendering.WinUI;

/// <summary>独立渲染线程。队列和帧唤醒共用信号，所有 continuation 异步执行。</summary>
internal sealed class RenderWorker : IAsyncDisposable
{
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly Action _render;
    private readonly Action<Exception> _failed;
    private readonly Thread _thread;
    private bool _stopping;
    private bool _accepting = true;

    public RenderWorker(Action render, Action<Exception> failed)
    {
        _render = render;
        _failed = failed;
        _thread = new Thread(Run) { IsBackground = true, Name = "MpvShell ANGLE 渲染" };
        _thread.Start();
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (!_accepting) return Task.FromException(new ObjectDisposedException(nameof(RenderWorker)));
            _commands.Enqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }
                try { action(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            _wake.Set();
        }
        return completion.Task;
    }

    // 供原生回调调用：不进入 UI、不等待锁，不让异常跨过 ABI。
    public void Wake()
    {
        try { _wake.Set(); }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_accepting)
            {
                _accepting = false;
                _commands.Enqueue(() => _stopping = true);
                _wake.Set();
            }
        }
        await _stopped.Task.ConfigureAwait(false);
    }

    private void Run()
    {
        try
        {
            while (true)
            {
                _wake.WaitOne();
                while (_commands.TryDequeue(out var command)) command();
                if (_stopping) return;
                try { _render(); }
                catch (Exception ex)
                {
                    // 故障通知只是跨线程信号，不能让订阅方异常终止清理线程。
                    try { _failed(ex); } catch { }
                }
            }
        }
        finally
        {
            _wake.Dispose();
            _stopped.TrySetResult();
        }
    }
}

using System.Collections.Concurrent;

namespace MpvShell.App.Tests;

/// <summary>为依赖 UI 线程的异步测试提供串行上下文，不创建真实窗口。</summary>
internal sealed class SingleThreadTestContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _callbacks = new();

    internal static Task RunAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var context = new SingleThreadTestContext();
            SetSynchronizationContext(context);
            try
            {
                var operation = action();
                _ = operation.ContinueWith(_ => context._callbacks.CompleteAdding(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                foreach (var callback in context._callbacks.GetConsumingEnumerable())
                    callback.Callback(callback.State);
                operation.GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { SetSynchronizationContext(null); }
        }) { IsBackground = true, Name = "RecentHistoryTestUi" };
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public override void Post(SendOrPostCallback callback, object? state) => _callbacks.Add((callback, state));
    public override SynchronizationContext CreateCopy() => this;
    public void Dispose() => _callbacks.Dispose();
}

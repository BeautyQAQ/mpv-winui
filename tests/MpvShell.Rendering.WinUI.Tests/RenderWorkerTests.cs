using System.Collections.Concurrent;
using FluentAssertions;

namespace MpvShell.Rendering.WinUI.Tests;

public sealed class RenderWorkerTests
{
    [Fact]
    public async Task Commands_and_frames_should_use_one_dedicated_thread_in_queue_order()
    {
        var commands = new ConcurrentQueue<int>();
        var threads = new ConcurrentBag<int>();
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var worker = new RenderWorker(() =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            frame.TrySetResult();
        }, _ => { });

        var tasks = Enumerable.Range(0, 20).Select(index => worker.InvokeAsync(() =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            commands.Enqueue(index);
        })).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        await frame.Task.WaitAsync(TimeSpan.FromSeconds(5));
        commands.Should().Equal(Enumerable.Range(0, 20));
        threads.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Canceled_or_failed_command_should_not_prevent_later_cleanup()
    {
        await using var worker = new RenderWorker(() => { }, _ => { });
        var canceledRan = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = () => worker.InvokeAsync(() => canceledRan = true, cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        canceledRan.Should().BeFalse();

        var failing = () => worker.InvokeAsync(() => throw new InvalidOperationException("模拟命令失败"));
        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("模拟命令失败");
        var cleaned = false;
        await worker.InvokeAsync(() => cleaned = true).WaitAsync(TimeSpan.FromSeconds(5));
        cleaned.Should().BeTrue();
    }

    [Fact]
    public async Task Frame_or_error_subscriber_failure_should_not_terminate_the_resource_owner_thread()
    {
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var worker = new RenderWorker(
            () => throw new InvalidOperationException("模拟设备丢失"),
            _ => { failed.TrySetResult(); throw new InvalidOperationException("模拟订阅方失败"); });
        worker.Wake();
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleaned = false;
        await worker.InvokeAsync(() => cleaned = true).WaitAsync(TimeSpan.FromSeconds(5));
        cleaned.Should().BeTrue();
    }

    [Fact]
    public async Task Disposal_should_drain_commands_be_repeatable_and_tolerate_late_native_wakeups()
    {
        var worker = new RenderWorker(() => { }, _ => { });
        var cleaned = false;
        var cleanup = worker.InvokeAsync(() => cleaned = true);
        await worker.DisposeAsync();
        await cleanup;
        await worker.DisposeAsync();
        worker.Wake();
        cleaned.Should().BeTrue();
        var enqueue = () => worker.InvokeAsync(() => { });
        await enqueue.Should().ThrowAsync<ObjectDisposedException>();
    }
}

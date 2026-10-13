using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class UiPlayerEventPumpTests
{
    [Fact]
    public async Task Snapshot_coalescing_should_preserve_last_arrival_order_and_every_terminal_boundary()
    {
        var callbacks = new Queue<Action>();
        var applied = new List<PlayerEvent>();
        var firstFault = new BackendFaulted("第一次错误");
        var secondFault = new BackendFaulted("第二次错误");
        var end = new EndReached();
        var latestTrack = new TracksChanged([new TrackInfo(2, "audio", null, "新音轨", true)]);
        var beforeFault = StateAt(2);
        var beforeEnd = StateAt(4);
        var afterEnd = StateAt(6);
        var buffering = new BufferingChanged(false);
        PlayerEvent[] events =
        [
            StateAt(1), new TracksChanged([]), beforeFault, latestTrack,
            firstFault, StateAt(3), beforeEnd, secondFault, end,
            StateAt(5), new BufferingChanged(true), afterEnd, buffering,
        ];

        var pump = UiPlayerEventPump.RunAsync(ReadyEvents(events), callbacks.Enqueue, applied.Add, CancellationToken.None);

        callbacks.Should().HaveCount(1);
        applied.Should().BeEmpty();
        callbacks.Dequeue()();
        await pump.WaitAsync(TimeSpan.FromSeconds(5));

        applied.Should().Equal(beforeFault, latestTrack, firstFault, beforeEnd, secondFault, end, afterEnd, buffering);
        callbacks.Should().BeEmpty();
    }

    [Fact]
    public async Task Blocked_ui_should_stop_draining_a_hot_event_source_after_a_bounded_batch()
    {
        using var cancellation = new CancellationTokenSource();
        var callbacks = new Queue<Action>();
        var applied = new List<PlayerEvent>();
        var readCount = 0;
        var pump = UiPlayerEventPump.RunAsync(
            ReadyEvents(Enumerable.Range(1, 10000).Select(StateAt), () => readCount++),
            callbacks.Enqueue, applied.Add, cancellation.Token);

        // RunAsync 在第一个未完成的 UI 回调处挂起；不靠 sleep 猜测工作线程进度。
        callbacks.Should().HaveCount(1);
        readCount.Should().BeGreaterThan(1).And.BeLessThan(10000);
        applied.Should().BeEmpty();
        pump.IsCompleted.Should().BeFalse();

        cancellation.Cancel();
        Func<Task> waitForPump = () => pump.WaitAsync(TimeSpan.FromSeconds(5));
        await waitForPump.Should().ThrowAsync<OperationCanceledException>();
        callbacks.Dequeue()();
        applied.Should().BeEmpty("取消后迟到的 UI 回调不能更新绑定状态");
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_ui_should_finish_pending_read_before_disposing_the_iterator()
    {
        using var cancellation = new CancellationTokenSource();
        var callbacks = new Queue<Action>();
        var applied = new List<PlayerEvent>();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readFinished = false;
        var disposed = false;
        var pump = UiPlayerEventPump.RunAsync(Source(), callbacks.Enqueue, applied.Add, cancellation.Token);
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callbacks.Should().HaveCount(1);

        cancellation.Cancel();
        Func<Task> waitForPump = () => pump.WaitAsync(TimeSpan.FromSeconds(5));
        await waitForPump.Should().ThrowAsync<OperationCanceledException>();

        readFinished.Should().BeTrue();
        disposed.Should().BeTrue();
        callbacks.Dequeue()();
        applied.Should().BeEmpty();

        async IAsyncEnumerable<PlayerEvent> Source([EnumeratorCancellation] CancellationToken token = default)
        {
            try
            {
                yield return StateAt(1);
                readStarted.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { readFinished = true; }
            }
            finally { disposed = true; }
        }
    }

    [Fact]
    public async Task Waiting_for_the_next_event_should_not_delay_an_already_ready_ui_update()
    {
        using var cancellation = new CancellationTokenSource();
        var events = Channel.CreateUnbounded<PlayerEvent>();
        var callbacks = Channel.CreateUnbounded<Action>();
        var applied = new List<PlayerEvent>();
        var state = StateAt(8);
        events.Writer.TryWrite(state);
        var pump = UiPlayerEventPump.RunAsync(events.Reader.ReadAllAsync(),
            callback => callbacks.Writer.TryWrite(callback), applied.Add, cancellation.Token);

        (await callbacks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))();
        applied.Should().Equal(state);
        pump.IsCompleted.Should().BeFalse();
        events.Writer.TryComplete();
        await pump.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Observation_failure_should_propagate_after_already_read_events_are_applied()
    {
        var events = Channel.CreateUnbounded<PlayerEvent>();
        var callbacks = new Queue<Action>();
        var applied = new List<PlayerEvent>();
        var state = StateAt(8);
        var failure = new InvalidOperationException("事件流中断");
        events.Writer.TryWrite(state);
        events.Writer.TryComplete(failure);
        var pump = UiPlayerEventPump.RunAsync(events.Reader.ReadAllAsync(), callbacks.Enqueue, applied.Add, CancellationToken.None);

        callbacks.Should().HaveCount(1);
        pump.IsCompleted.Should().BeFalse();
        callbacks.Dequeue()();
        Func<Task> waitForPump = () => pump.WaitAsync(TimeSpan.FromSeconds(5));
        var observed = await waitForPump.Should().ThrowExactlyAsync<InvalidOperationException>();

        observed.Which.Should().BeSameAs(failure);
        applied.Should().Equal(state);
    }

    [Fact]
    public async Task Ui_apply_failure_should_cancel_the_pending_read_and_propagate_without_hanging()
    {
        var events = Channel.CreateUnbounded<PlayerEvent>();
        var callbacks = new Queue<Action>();
        var failure = new InvalidOperationException("UI 更新失败");
        events.Writer.TryWrite(StateAt(8));
        var pump = UiPlayerEventPump.RunAsync(events.Reader.ReadAllAsync(), callbacks.Enqueue,
            _ => throw failure, CancellationToken.None);

        callbacks.Should().HaveCount(1);
        callbacks.Dequeue()();
        Func<Task> waitForPump = () => pump.WaitAsync(TimeSpan.FromSeconds(5));
        var observed = await waitForPump.Should().ThrowExactlyAsync<InvalidOperationException>();

        observed.Which.Should().BeSameAs(failure);
    }

    private static PlaybackStateChanged StateAt(int seconds) =>
        new(PlaybackState.Initial with { CurrentUrl = "current.mp4", PositionSeconds = seconds });

    private static async IAsyncEnumerable<PlayerEvent> ReadyEvents(IEnumerable<PlayerEvent> events, Action? onRead = null)
    {
        await Task.CompletedTask;
        foreach (var playerEvent in events)
        {
            onRead?.Invoke();
            yield return playerEvent;
        }
    }
}

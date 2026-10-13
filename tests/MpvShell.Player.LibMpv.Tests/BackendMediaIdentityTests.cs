using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class BackendMediaIdentityTests
{
    [Fact]
    public async Task Successive_canceled_loads_should_not_complete_or_overwrite_the_final_media()
    {
        await using var first = new HttpMediaServer(holdMedia: true, durationSeconds: 7);
        await using var second = new HttpMediaServer(holdMedia: true, durationSeconds: 11);
        await using var final = new HttpMediaServer(holdMedia: true, durationSeconds: 3);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(timeout.Token);
            var firstLoad = backend.LoadUrlAsync(first.MediaUrl, firstCancellation.Token);
            await first.MediaRequested.WaitAsync(timeout.Token);
            firstCancellation.Cancel();
            Func<Task> waitFirst = () => firstLoad;
            await waitFirst.Should().ThrowAsync<OperationCanceledException>();

            var secondLoad = backend.LoadUrlAsync(second.MediaUrl, secondCancellation.Token);
            await second.MediaRequested.WaitAsync(timeout.Token);
            secondCancellation.Cancel();
            Func<Task> waitSecond = () => secondLoad;
            await waitSecond.Should().ThrowAsync<OperationCanceledException>();

            var finalLoad = backend.LoadUrlAsync(final.MediaUrl, timeout.Token);
            await final.MediaRequested.WaitAsync(timeout.Token);
            // A/B 的响应在 C 已实际请求、但尚未取得媒体头时才允许返回。
            // C 的成功不能来自先前被取消的调用方等待或它们的迟到完成。
            first.ReleaseMedia();
            second.ReleaseMedia();
            (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(final.MediaUrl);
            finalLoad.IsCompleted.Should().BeFalse("最终媒体的响应仍被屏障阻塞，不能提前完成加载");

            final.ReleaseMedia();
            await finalLoad.WaitAsync(timeout.Token);
            await backend.PauseAsync(timeout.Token);
            await backend.SetVolumeAsync(35, timeout.Token);
            var events = await ReadThroughVolumeAsync(backend, 35, timeout.Token);
            var state = events.OfType<PlaybackStateChanged>().Last().State;
            state.CurrentUrl.Should().Be(final.MediaUrl);
            state.DurationSeconds.Should().BeApproximately(3, 0.01);
            events.OfType<BackendFaulted>().Should().BeEmpty();
            (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(final.MediaUrl);
            Convert.ToDouble(await session.GetPropertyAsync("duration", timeout.Token)).Should().BeApproximately(3, 0.01);
            (await backend.GetTracksAsync(timeout.Token)).Should().ContainSingle(track => track.Kind == "audio");
        }
        finally
        {
            first.ReleaseMedia();
            second.ReleaseMedia();
            final.ReleaseMedia();
        }
    }

    [Fact]
    public async Task A_delayed_native_start_delivery_should_finish_before_the_next_load_is_submitted()
    {
        await using var first = new HttpMediaServer(holdMedia: true, durationSeconds: 7);
        await using var next = new HttpMediaServer(durationSeconds: 3);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var firstCancellation = new CancellationTokenSource();
        using var releaseStart = new ManualResetEventSlim();
        var startBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCount = 0;
        await using var session = CreateSession();
        // 先于 backend 订阅，暂停一次真实 StartFile 的送达；不生成或调换原生事件。
        session.EventReceived += DelayFirstStart;
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(timeout.Token);
            var firstLoad = backend.LoadUrlAsync(first.MediaUrl, firstCancellation.Token);
            await startBlocked.Task.WaitAsync(timeout.Token);
            firstCancellation.Cancel();
            Func<Task> waitFirst = () => firstLoad;
            await waitFirst.Should().ThrowAsync<OperationCanceledException>();

            var nextLoad = backend.LoadUrlAsync(next.MediaUrl, timeout.Token);
            nextLoad.IsCompleted.Should().BeFalse();
            next.MediaRequested.IsCompleted.Should().BeFalse(
                "事件线程尚未送达旧 StartFile，下一次解除暂停请求还不能收到回复并提交 loadfile");
            Volatile.Read(ref startCount).Should().Be(1);

            releaseStart.Set();
            await nextLoad.WaitAsync(timeout.Token);
            first.ReleaseMedia();
            await backend.PauseAsync(timeout.Token);
            await backend.SetVolumeAsync(36, timeout.Token);
            var events = await ReadThroughVolumeAsync(backend, 36, timeout.Token);
            var state = events.OfType<PlaybackStateChanged>().Last().State;
            state.CurrentUrl.Should().Be(next.MediaUrl);
            state.DurationSeconds.Should().BeApproximately(3, 0.01);
            events.OfType<BackendFaulted>().Should().BeEmpty();
            (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(next.MediaUrl);
            Volatile.Read(ref startCount).Should().Be(2);
        }
        finally
        {
            // 必须先无条件放行事件线程，随后才允许 backend/session 的异步释放。
            releaseStart.Set();
            first.ReleaseMedia();
            session.EventReceived -= DelayFirstStart;
        }

        void DelayFirstStart(SessionEvent item)
        {
            if (item.Id != MpvEventId.StartFile || Interlocked.Increment(ref startCount) != 1) return;
            startBlocked.TrySetResult();
            releaseStart.Wait(timeout.Token);
        }
    }

    private static MpvPlayerSession CreateSession() => new(new Dictionary<string, string> { ["ao"] = "null" });

    private static async Task<List<PlayerEvent>> ReadThroughVolumeAsync(
        LibMpvBackend backend, int volume, CancellationToken cancellationToken)
    {
        var events = new List<PlayerEvent>();
        await foreach (var item in backend.ObserveEventsAsync(cancellationToken))
        {
            events.Add(item);
            if (item is PlaybackStateChanged state && state.State.Volume == volume) return events;
        }
        throw new InvalidOperationException("事件流在最终状态标记到达前结束。");
    }
}

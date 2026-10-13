using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class BackendLoadCancellationTests
{
    [Fact]
    public async Task Canceling_a_load_waiting_for_recovery_should_preserve_the_current_media()
    {
        await using var first = new HttpMediaServer();
        await using var next = new HttpMediaServer();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var canceled = new CancellationTokenSource();
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        await backend.LoadUrlAsync(first.MediaUrl, timeout.Token);
        var lease = await session.SuspendVideoForRecoveryAsync(timeout.Token);
        try
        {
            var load = backend.LoadUrlAsync(next.MediaUrl, canceled.Token);
            load.IsCompleted.Should().BeFalse();
            canceled.Cancel();
            Func<Task> wait = () => load;
            await wait.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { await session.AbortVideoRecoveryAsync(lease); }

        await backend.SetVolumeAsync(31, timeout.Token);
        var events = await ReadUntilAsync(backend,
            item => item is PlaybackStateChanged { State.Volume: 31 }, timeout.Token);
        events.OfType<PlaybackStateChanged>().Last().State.CurrentUrl.Should().Be(first.MediaUrl,
            "尚未提交的加载被取消后，当前媒体仍应来自实际播放的文件");
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(first.MediaUrl);
        next.MediaRequested.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task A_queued_replacement_should_not_swallow_the_canceled_load_actual_completion()
    {
        await using var first = new HttpMediaServer(holdMedia: true);
        await using var next = new HttpMediaServer();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var firstCancel = new CancellationTokenSource();
        using var nextCancel = new CancellationTokenSource();
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        var firstLoad = backend.LoadUrlAsync(first.MediaUrl, firstCancel.Token);
        await first.MediaRequested.WaitAsync(timeout.Token);
        firstCancel.Cancel();
        Func<Task> waitFirst = () => firstLoad;
        await waitFirst.Should().ThrowAsync<OperationCanceledException>();

        var lease = await session.SuspendVideoForRecoveryAsync(timeout.Token);
        try
        {
            var nextLoad = backend.LoadUrlAsync(next.MediaUrl, nextCancel.Token);
            nextLoad.IsCompleted.Should().BeFalse();
            var fileLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.EventReceived += item =>
            {
                if (item.Id == MpvEventId.FileLoaded) fileLoaded.TrySetResult();
            };
            first.ReleaseMedia();
            await fileLoaded.Task.WaitAsync(timeout.Token);
            nextCancel.Cancel();
            Func<Task> waitNext = () => nextLoad;
            await waitNext.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { await session.AbortVideoRecoveryAsync(lease); }

        await backend.PlayAsync(timeout.Token);
        await backend.SetVolumeAsync(32, timeout.Token);
        var events = await ReadUntilAsync(backend,
            item => item is PlaybackStateChanged { State.Volume: 32 }, timeout.Token);
        var state = events.OfType<PlaybackStateChanged>().Last().State;
        state.CurrentUrl.Should().Be(first.MediaUrl);
        state.IsPlaying.Should().BeTrue("取消的是调用方等待，已提交媒体的 FileLoaded 仍须生效");
        next.MediaRequested.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task A_load_timeout_should_only_end_the_wait_and_allow_later_native_completion()
    {
        await using var server = new HttpMediaServer(holdMedia: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await using var session = new MpvPlayerSession(new Dictionary<string, string>
        {
            ["ao"] = "null", ["network-timeout"] = "30",
        });
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        var load = backend.LoadUrlAsync(server.MediaUrl, timeout.Token);
        await server.MediaRequested.WaitAsync(timeout.Token);
        Func<Task> wait = () => load;
        await wait.Should().ThrowAsync<TimeoutException>().WithMessage("加载媒体超时*");

        server.ReleaseMedia();
        var events = await ReadUntilAsync(backend,
            item => item is PlaybackStateChanged { State.IsPlaying: true }, timeout.Token);
        events.OfType<PlaybackStateChanged>().Last().State.CurrentUrl.Should().Be(server.MediaUrl);
        events.OfType<BackendFaulted>().Should().BeEmpty();
        await backend.PauseAsync(timeout.Token);
        await backend.PlayAsync(timeout.Token);
    }

    [Fact]
    public async Task A_submitted_replacement_should_complete_with_its_own_media_after_canceling_the_previous_wait()
    {
        await using var first = new HttpMediaServer(holdMedia: true);
        await using var next = new HttpMediaServer(durationSeconds: 3);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var canceled = new CancellationTokenSource();
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        var firstLoad = backend.LoadUrlAsync(first.MediaUrl, canceled.Token);
        await first.MediaRequested.WaitAsync(timeout.Token);
        canceled.Cancel();
        Func<Task> wait = () => firstLoad;
        await wait.Should().ThrowAsync<OperationCanceledException>();

        await backend.LoadUrlAsync(next.MediaUrl, timeout.Token);
        first.ReleaseMedia();
        await backend.SetVolumeAsync(33, timeout.Token);
        var events = await ReadUntilAsync(backend,
            item => item is PlaybackStateChanged { State.Volume: 33 }, timeout.Token);
        var state = events.OfType<PlaybackStateChanged>().Last().State;
        state.CurrentUrl.Should().Be(next.MediaUrl);
        state.DurationSeconds.Should().BeApproximately(3, 0.01);
        events.OfType<BackendFaulted>().Should().BeEmpty();
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(next.MediaUrl);
    }

    private static MpvPlayerSession CreateSession() => new(new Dictionary<string, string> { ["ao"] = "null" });

    private static async Task<List<PlayerEvent>> ReadUntilAsync(LibMpvBackend backend,
        Func<PlayerEvent, bool> predicate, CancellationToken cancellationToken)
    {
        var events = new List<PlayerEvent>();
        await foreach (var item in backend.ObserveEventsAsync(cancellationToken))
        {
            events.Add(item);
            if (predicate(item)) return events;
        }
        throw new InvalidOperationException("事件流在预期状态到达前结束。");
    }
}

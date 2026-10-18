using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class BackendStopAndSpeedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_should_unload_playing_or_paused_media_clear_details_and_allow_replay(bool paused)
    {
        await using var media = new HttpMediaServer(durationSeconds: 30);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        await backend.SetSpeedAsync(1.5, timeout.Token);
        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        await backend.SetPositionAsync(8, timeout.Token);
        await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.PositionSeconds: >= 7 }, timeout.Token);
        if (paused) await backend.PauseAsync(timeout.Token);
        (await backend.GetTracksAsync(timeout.Token)).Should().NotBeEmpty();
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "paused-for-cache", true));

        var nativeEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.EventReceived += item => { if (item.Id == MpvEventId.EndFile) nativeEnd.TrySetResult(); };
        await backend.StopAsync(timeout.Token);
        await nativeEnd.Task.WaitAsync(timeout.Token);
        await backend.SetVolumeAsync(37, timeout.Token);
        var events = await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Volume: 37 }, timeout.Token);
        AssertStopped(events.OfType<PlaybackStateChanged>().Last().State, media.MediaUrl, 1.5);
        events.OfType<EndReached>().Should().BeEmpty();
        events.OfType<BackendFaulted>().Should().BeEmpty();
        events.OfType<BufferingChanged>().Last().IsBuffering.Should().BeFalse();
        (await backend.GetTracksAsync(timeout.Token)).Should().BeEmpty();
        (await backend.GetInfoSnapshotAsync(timeout.Token)).Should().Be(new InfoPanelSnapshot(null, null, null, null, null, null, null));
        (await session.GetPropertyAsync("playlist-count", timeout.Token)).Should().Be(0L);
        (await session.GetPropertyAsync("idle-active", timeout.Token)).Should().Be(true);

        await backend.StopAsync(timeout.Token); // 重复停止仍是可用会话。
        await backend.PlayAsync(timeout.Token);
        var resumed = await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.IsPlaying: true }, timeout.Token);
        var playing = resumed.OfType<PlaybackStateChanged>().Last().State;
        playing.IsStopped.Should().BeFalse();
        playing.PositionSeconds.Should().BeLessThan(2);
        playing.Speed.Should().Be(1.5);
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(media.MediaUrl);
    }

    [Fact]
    public async Task Stop_should_cancel_a_held_load_and_previously_queued_load_without_waiting_for_network()
    {
        await using var held = new HttpMediaServer(holdMedia: true);
        await using var queuedMedia = new HttpMediaServer(durationSeconds: 10);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        var load = backend.LoadUrlAsync(held.MediaUrl, timeout.Token);
        await held.MediaRequested.WaitAsync(timeout.Token);
        var queued = backend.LoadUrlAsync(queuedMedia.MediaUrl, timeout.Token);

        await backend.StopAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);
        Func<Task> waitLoad = () => load.WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);
        Func<Task> waitQueued = () => queued.WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);
        await waitLoad.Should().ThrowAsync<OperationCanceledException>();
        await waitQueued.Should().ThrowAsync<OperationCanceledException>();
        queuedMedia.MediaRequested.IsCompleted.Should().BeFalse();
        held.ReleaseMedia();
        await backend.SetVolumeAsync(38, timeout.Token);
        var stopped = await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Volume: 38 }, timeout.Token);
        AssertStopped(stopped.OfType<PlaybackStateChanged>().Last().State, held.MediaUrl, 1);
        stopped.OfType<BackendFaulted>().Should().BeEmpty();
        stopped.OfType<EndReached>().Should().BeEmpty();

        await backend.LoadUrlAsync(queuedMedia.MediaUrl, timeout.Token);
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(queuedMedia.MediaUrl);
    }

    [Fact]
    public async Task Late_media_events_after_stop_must_not_restore_media_eof_or_errors()
    {
        await using var media = new HttpMediaServer(durationSeconds: 30);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        await backend.StopAsync(timeout.Token);
        backend.OnSessionEvent(new(MpvEventId.StartFile, PlaylistEntryId: 1));
        backend.OnSessionEvent(new(MpvEventId.FileLoaded));
        backend.OnSessionEvent(new(MpvEventId.EndFile, Value: 0));
        backend.OnSessionEvent(new(MpvEventId.EndFile, Value: 4, Error: new InvalidOperationException("旧加载失败")));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "time-pos", 23.0));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "duration", 99.0));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "seekable", true));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "video-codec", "hevc"));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "paused-for-cache", true));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "eof-reached", true));
        await backend.SetSpeedAsync(2, timeout.Token);
        await backend.SetVolumeAsync(39, timeout.Token);
        var events = await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Volume: 39 }, timeout.Token);
        AssertStopped(events.OfType<PlaybackStateChanged>().Last().State, media.MediaUrl, 2);
        events.OfType<BackendFaulted>().Should().BeEmpty();
        events.OfType<EndReached>().Should().BeEmpty();
        (await backend.GetInfoSnapshotAsync(timeout.Token)).VideoCodec.Should().BeNull();
    }

    [Fact]
    public async Task Native_speed_changes_should_be_observed_and_preserved_across_media_and_eof()
    {
        await using var first = new HttpMediaServer(durationSeconds: 12);
        await using var second = new HttpMediaServer(durationSeconds: 2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        await backend.SetSpeedAsync(0.25, timeout.Token);
        await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Speed: 0.25 }, timeout.Token);
        await backend.LoadUrlAsync(first.MediaUrl, timeout.Token);
        await backend.PauseAsync(timeout.Token);
        // 外部原生属性修改也必须同步到后端，不能仅由命令路径乐观更新。
        await session.SetPropertyAsync("speed", 4.0, timeout.Token);
        await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Speed: 4 }, timeout.Token);
        await backend.LoadUrlAsync(second.MediaUrl, timeout.Token);
        (await session.GetPropertyAsync("speed", timeout.Token)).Should().Be(4.0);
        await ReadUntilAsync(backend, item => item is EndReached, timeout.Token);
        await backend.SetVolumeAsync(40, timeout.Token);
        var ended = await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Volume: 40 }, timeout.Token);
        ended.OfType<PlaybackStateChanged>().Last().State.Speed.Should().Be(4);
        await backend.PlayAsync(timeout.Token);
        (await session.GetPropertyAsync("speed", timeout.Token)).Should().Be(4.0);
        await backend.StopAsync(timeout.Token);
        (await session.GetPropertyAsync("speed", timeout.Token)).Should().Be(4.0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(0.249)]
    [InlineData(4.001)]
    public async Task Invalid_speed_should_be_rejected_without_changing_native_speed(double speed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        Func<Task> invalid = () => backend.SetSpeedAsync(speed, timeout.Token);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await session.GetPropertyAsync("speed", timeout.Token)).Should().Be(1.0);
    }

    [Fact]
    public async Task Stop_queued_during_load_submission_must_finish_after_the_load_without_restarting_it()
    {
        await using var held = new HttpMediaServer(holdMedia: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        // 图形恢复租约阻止 pause 的原生提交，精确覆盖 Load 已开始但 loadfile 尚未提交的窗口。
        var lease = await session.SuspendVideoForRecoveryAsync(timeout.Token);
        var load = backend.LoadUrlAsync(held.MediaUrl, timeout.Token);
        var stop = backend.StopAsync(timeout.Token);
        try
        {
            stop.IsCompleted.Should().BeFalse();
            await session.AbortVideoRecoveryAsync(lease);
            await stop.WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);
            Func<Task> wait = () => load.WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);
            await wait.Should().ThrowAsync<OperationCanceledException>();
            await backend.SetVolumeAsync(41, timeout.Token);
            var events = await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Volume: 41 }, timeout.Token);
            AssertStopped(events.OfType<PlaybackStateChanged>().Last().State, held.MediaUrl, 1);
            events.OfType<BackendFaulted>().Should().BeEmpty();
        }
        finally
        {
            await session.AbortVideoRecoveryAsync(lease);
            held.ReleaseMedia();
        }
    }

    [Fact]
    public async Task Stop_cancelled_while_waiting_for_a_recovery_lease_must_not_unload_media()
    {
        await using var media = new HttpMediaServer(durationSeconds: 30);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancellation = new CancellationTokenSource();
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        var lease = await session.SuspendVideoForRecoveryAsync(timeout.Token);
        try
        {
            var stop = backend.StopAsync(cancellation.Token);
            cancellation.Cancel();
            Func<Task> wait = () => stop;
            await wait.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { await session.AbortVideoRecoveryAsync(lease); }
        await backend.SetVolumeAsync(42, timeout.Token);
        var events = await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Volume: 42 }, timeout.Token);
        events.OfType<PlaybackStateChanged>().Last().State.IsStopped.Should().BeFalse();
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(media.MediaUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_must_wait_for_native_unload_and_still_confirm_after_its_caller_cancels(bool cancelWait)
    {
        await using var media = new HttpMediaServer(durationSeconds: 30);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancellation = new CancellationTokenSource();
        using var releaseEnd = new ManualResetEventSlim();
        var endBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = CreateSession();
        session.EventReceived += BlockEnd;
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(timeout.Token);
            await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
            await backend.SetVolumeAsync(43, timeout.Token);
            await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Volume: 43 }, timeout.Token);
            var stop = backend.StopAsync(cancellation.Token);
            await endBlocked.Task.WaitAsync(timeout.Token);
            stop.IsCompleted.Should().BeFalse("受理命令不能提前宣称已经卸载媒体");
            (await backend.GetTracksAsync(timeout.Token)).Should().NotBeEmpty();
            Func<Task> seek = () => backend.SeekAsync(3, timeout.Token);
            Func<Task> position = () => backend.SetPositionAsync(3, timeout.Token);
            await seek.Should().ThrowAsync<InvalidOperationException>().WithMessage("*已停止*");
            await position.Should().ThrowAsync<InvalidOperationException>().WithMessage("*已停止*");
            // 原生事件线程仍被屏障挡住；全局属性变化不得顺带发布提前停止的状态。
            backend.OnSessionEvent(new(MpvEventId.PropertyChange, "volume", 44L));
            var pending = await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.Volume: 44 }, timeout.Token);
            pending.OfType<PlaybackStateChanged>().Last().State.IsStopped.Should().BeFalse();
            if (cancelWait)
            {
                cancellation.Cancel();
                Func<Task> wait = () => stop;
                await wait.Should().ThrowAsync<OperationCanceledException>();
            }
            releaseEnd.Set();
            if (!cancelWait) await stop;
            var completed = await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.IsStopped: true }, timeout.Token);
            AssertStopped(completed.OfType<PlaybackStateChanged>().Last().State, media.MediaUrl, 1);
            (await backend.GetTracksAsync(timeout.Token)).Should().BeEmpty();
            await position.Should().ThrowAsync<InvalidOperationException>().WithMessage("*已停止*");
            Func<Task> path = () => session.GetPropertyAsync("path", timeout.Token);
            (await path.Should().ThrowAsync<MpvException>()).Which.ErrorCode.Should().Be(-10);
        }
        finally
        {
            releaseEnd.Set();
            session.EventReceived -= BlockEnd;
        }

        void BlockEnd(SessionEvent item)
        {
            if (item.Id != MpvEventId.EndFile) return;
            endBlocked.TrySetResult();
            releaseEnd.Wait(timeout.Token);
        }
    }

    [Fact]
    public async Task Stop_before_open_is_idempotent_and_a_cancelled_stop_does_not_unload_media()
    {
        await using var media = new HttpMediaServer(durationSeconds: 30);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        await backend.StopAsync(timeout.Token);
        await backend.StopAsync(timeout.Token);
        Func<Task> play = () => backend.PlayAsync(timeout.Token);
        await play.Should().ThrowAsync<InvalidOperationException>();
        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        Func<Task> stop = () => backend.StopAsync(new CancellationToken(true));
        await stop.Should().ThrowAsync<OperationCanceledException>();
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(media.MediaUrl);
        (await session.GetPropertyAsync("idle-active", timeout.Token)).Should().Be(false);
    }

    [Fact]
    public async Task Terminal_backend_should_reject_stop_and_speed_without_reviving_playback()
    {
        await using var backend = new LibMpvBackend(new MpvPlayerSession());
        backend.OnSessionEvent(new(MpvEventId.Shutdown, Error: new InvalidOperationException("会话结束")));
        Func<Task> stop = () => backend.StopAsync(CancellationToken.None);
        Func<Task> speed = () => backend.SetSpeedAsync(2, CancellationToken.None);
        await stop.Should().ThrowAsync<InvalidOperationException>();
        await speed.Should().ThrowAsync<InvalidOperationException>();
    }

    private static MpvPlayerSession CreateSession() => new(new Dictionary<string, string> { ["ao"] = "null" });

    private static void AssertStopped(PlaybackState state, string source, double speed)
    {
        state.IsStopped.Should().BeTrue();
        state.IsSeekable.Should().BeFalse();
        state.IsPlaying.Should().BeFalse();
        state.PositionSeconds.Should().Be(0);
        state.DurationSeconds.Should().Be(0);
        state.CurrentUrl.Should().Be(source);
        state.Speed.Should().Be(speed);
    }

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

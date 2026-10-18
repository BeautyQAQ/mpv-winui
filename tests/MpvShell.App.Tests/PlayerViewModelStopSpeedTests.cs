using System.Threading.Channels;
using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelStopSpeedTests
{
    [Fact]
    public async Task Stop_should_wait_for_backend_state_and_clear_media_details_when_stopped()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { StopHandler = _ => stopped.Task };
        var ui = new TestDispatcher();
        await using var vm = CreateViewModel(backend, ui);
        await vm.InitializeAsync();
        vm.Tracks.Add(new(1, "audio", null, "原音轨", true));
        vm.InfoPanel.Update(new("hevc", "aac", "HDR10", "3840x2160", "10-bit", "24", "8s"));
        await ui.ApplyAsync(backend, new BufferingChanged(true));
        var request = vm.StopCommand.ExecuteAsync(null);

        vm.IsStopping.Should().BeTrue();
        vm.State.IsStopped.Should().BeFalse();
        stopped.SetResult();
        await request;
        vm.State.IsStopped.Should().BeFalse("命令回复不能替代实际状态");
        vm.State.PositionSeconds.Should().Be(20);

        await ui.ApplyAsync(backend, new PlaybackStateChanged(vm.State with
        {
            IsStopped = true, IsPlaying = false, PositionSeconds = 0, DurationSeconds = 0,
        }));

        vm.IsStopping.Should().BeFalse();
        vm.State.CurrentUrl.Should().Be("current.mp4");
        vm.State.Speed.Should().Be(1.5);
        vm.State.PositionSeconds.Should().Be(0);
        vm.Tracks.Should().BeEmpty();
        vm.InfoPanel.VideoSummary.Should().NotContain("hevc");
        vm.IsBuffering.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_failure_should_leave_playback_intact_and_show_the_error()
    {
        var backend = new EventPublishingBackend { StopHandler = _ => Task.FromException(new InvalidOperationException("停止失败")) };
        await using var vm = CreateViewModel(backend);
        var original = vm.State;

        await vm.StopCommand.ExecuteAsync(null);

        vm.State.Should().Be(original with { AreControlsVisible = true });
        vm.ErrorMessage.Should().Be("停止失败");
        vm.IsStopping.Should().BeFalse();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("canceled")]
    public async Task Stopping_a_pending_load_should_cancel_it_and_ignore_its_late_outcome(string outcome)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken loadToken = default;
        var backend = new EventPublishingBackend { LoadHandler = (_, token) => { loadToken = token; return loaded.Task; } };
        await using var vm = CreateViewModel(backend);
        vm.UrlText = "https://media.example/next.mp4";
        var load = vm.OpenUrlCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        loadToken.IsCancellationRequested.Should().BeTrue();
        if (outcome == "failure") loaded.SetException(new InvalidOperationException("迟到的加载失败"));
        else if (outcome == "canceled") loaded.SetCanceled();
        else loaded.SetResult();
        await load;

        vm.RecentUrls.Should().BeEmpty();
        vm.Tracks.Should().BeEmpty();
        vm.State.CurrentUrl.Should().Be("current.mp4");
        vm.ErrorMessage.Should().BeNull("用户停止加载不应展示取消或旧加载错误");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stopping_during_post_load_queries_should_not_restore_details_or_add_history(bool waitingForTracks)
    {
        var info = new TaskCompletionSource<InfoPanelSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracks = new TaskCompletionSource<IReadOnlyList<TrackInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (waitingForTracks) info.SetResult(new("hevc", null, null, null, null, null, null));
        var backend = new EventPublishingBackend { InfoHandler = _ => info.Task, TracksHandler = _ => tracks.Task };
        await using var vm = CreateViewModel(backend);
        vm.UrlText = "https://media.example/next.mp4";
        var load = vm.OpenUrlCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        info.TrySetResult(new("hevc", null, null, null, null, null, null));
        tracks.SetResult([new(1, "audio", null, "迟到的音轨", true)]);
        await load;

        vm.RecentUrls.Should().BeEmpty();
        vm.Tracks.Should().BeEmpty();
        vm.InfoPanel.VideoSummary.Should().NotContain("hevc");
    }

    [Theory]
    [InlineData("seek", false)]
    [InlineData("seek", true)]
    [InlineData("play", false)]
    [InlineData("play", true)]
    [InlineData("pause", false)]
    [InlineData("pause", true)]
    [InlineData("track", false)]
    [InlineData("track", true)]
    public async Task A_stop_should_discard_older_transport_results(string command, bool fails)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend
        {
            SetPositionHandler = (_, _) => completion.Task, PlayHandler = _ => completion.Task,
            PauseHandler = _ => completion.Task, AudioTrackHandler = (_, _) => completion.Task,
        };
        var ui = new TestDispatcher();
        await using var vm = CreateViewModel(backend, ui);
        await vm.InitializeAsync();
        vm.State = vm.State with { IsPlaying = command != "play" };
        var track = new TrackInfo(1, "audio", null, "旧音轨", false);
        vm.Tracks.Add(track);
        var pending = command switch
        {
            "seek" => vm.SeekToAsync(70),
            "track" => vm.SelectTrackCommand.ExecuteAsync(track),
            _ => vm.TogglePlayPauseCommand.ExecuteAsync(null),
        };
        await vm.StopCommand.ExecuteAsync(null);
        await ui.ApplyAsync(backend, new PlaybackStateChanged(vm.State with
        {
            IsStopped = true, IsPlaying = false, PositionSeconds = 0, DurationSeconds = 0,
        }));
        var stopped = vm.State;
        if (fails) completion.SetException(new InvalidOperationException("旧操作失败"));
        else completion.SetResult();
        await pending;

        vm.State.Should().Be(stopped);
        vm.Tracks.Should().BeEmpty();
        vm.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_delayed_stop_or_end_event_should_not_cancel_a_new_load(bool endEvent)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        var backend = new EventPublishingBackend { LoadHandler = (_, value) => { token = value; return loaded.Task; } };
        var ui = new TestDispatcher();
        await using var vm = CreateViewModel(backend, ui);
        await vm.InitializeAsync();
        var previousState = vm.State;
        await vm.StopCommand.ExecuteAsync(null);
        vm.UrlText = "https://media.example/next.mp4";
        var load = vm.OpenUrlCommand.ExecuteAsync(null);

        await ui.ApplyAsync(backend, endEvent ? new EndReached() : new PlaybackStateChanged(previousState with
        {
            IsStopped = true, IsPlaying = false, PositionSeconds = 0, DurationSeconds = 0,
        }));
        token.IsCancellationRequested.Should().BeFalse("已排队的旧媒体事件不属于刚提交的新加载");
        loaded.SetResult();
        await load;

        vm.RecentUrls.Should().ContainSingle().Which.Should().Be("https://media.example/next.mp4");
        vm.State.CurrentUrl.Should().Be("https://media.example/next.mp4");
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Stopped_media_should_reject_seek_and_track_but_allow_play_from_the_retained_url()
    {
        var playCalls = 0;
        var mediaCalls = 0;
        var backend = new EventPublishingBackend
        {
            PlayHandler = _ => { playCalls++; return Task.CompletedTask; },
            SeekHandler = (_, _) => { mediaCalls++; return Task.CompletedTask; },
            SetPositionHandler = (_, _) => { mediaCalls++; return Task.CompletedTask; },
            AudioTrackHandler = (_, _) => { mediaCalls++; return Task.CompletedTask; },
        };
        await using var vm = CreateViewModel(backend);
        vm.State = PlaybackState.Initial with { CurrentUrl = "current.mp4" };
        await vm.SeekToAsync(30);
        await vm.SeekForwardCommand.ExecuteAsync(null);
        await vm.SelectTrackCommand.ExecuteAsync(new TrackInfo(1, "audio", null, null, false));
        await vm.TogglePlayPauseCommand.ExecuteAsync(null);

        mediaCalls.Should().Be(0);
        playCalls.Should().Be(1);
        vm.State.IsPlaying.Should().BeFalse("重新打开成功须由后端状态事件确认");
    }

    [Fact]
    public async Task Speed_should_follow_native_state_even_when_the_command_finishes_later()
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        double requested = 0;
        var backend = new EventPublishingBackend { SpeedHandler = (speed, _) => { requested = speed; return changed.Task; } };
        var ui = new TestDispatcher();
        await using var vm = CreateViewModel(backend, ui);
        await vm.InitializeAsync();
        var request = vm.ChangeSpeedAsync(2);
        requested.Should().Be(2);
        vm.State.Speed.Should().Be(1.5);
        await ui.ApplyAsync(backend, new PlaybackStateChanged(vm.State with { Speed = 1.75 }));
        changed.SetResult();
        await request;

        vm.State.Speed.Should().Be(1.75);
        vm.SpeedLabel.Should().Be("倍速 1.75×");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.24)]
    [InlineData(4.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task Invalid_speed_should_not_reach_the_backend(double speed)
    {
        var calls = 0;
        var backend = new EventPublishingBackend { SpeedHandler = (_, _) => { calls++; return Task.CompletedTask; } };
        await using var vm = CreateViewModel(backend);
        await vm.ChangeSpeedAsync(speed);
        calls.Should().Be(0);
        vm.State.Speed.Should().Be(1.5);
        vm.ErrorMessage.Should().Be("播放倍速须在 0.25× 至 4× 之间。");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_older_speed_result_should_not_clear_the_latest_failure(bool olderFails)
    {
        var older = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend
        {
            SpeedHandler = (speed, _) => speed == 2 ? older.Task : Task.FromException(new InvalidOperationException("最新倍速失败")),
        };
        await using var vm = CreateViewModel(backend);
        var first = vm.ChangeSpeedAsync(2);
        await vm.ChangeSpeedAsync(3);
        if (olderFails) older.SetException(new InvalidOperationException("旧倍速失败"));
        else older.SetResult();
        await first;
        vm.State.Speed.Should().Be(1.5);
        vm.ErrorMessage.Should().Be("最新倍速失败");
    }

    [Theory]
    [InlineData("stop", false)]
    [InlineData("stop", true)]
    [InlineData("speed", false)]
    [InlineData("speed", true)]
    public async Task A_rendering_failure_should_survive_late_stop_or_speed_results(string command, bool fails)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { StopHandler = _ => pending.Task, SpeedHandler = (_, _) => pending.Task };
        await using var vm = CreateViewModel(backend);
        var request = command == "stop" ? vm.StopCommand.ExecuteAsync(null) : vm.ChangeSpeedAsync(2);
        vm.ReportRenderingFailure("渲染器已关闭");
        var terminal = vm.State;
        if (fails) pending.SetException(new InvalidOperationException("迟到的操作失败"));
        else pending.SetResult();
        await request;
        vm.State.Should().Be(terminal);
        vm.ErrorMessage.Should().Be("渲染器已关闭");
    }

    private static PlayerViewModel CreateViewModel(EventPublishingBackend backend, TestDispatcher? ui = null)
    {
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator())
        {
            State = PlaybackState.Initial with
            {
                CurrentUrl = "current.mp4", IsStopped = false, IsSeekable = true, IsPlaying = true,
                PositionSeconds = 20, DurationSeconds = 120, Speed = 1.5,
            },
        };
        if (ui is not null) vm.SetUiDispatcher(ui.Enqueue);
        return vm;
    }

    private sealed class TestDispatcher
    {
        private readonly Channel<Action> _callbacks = Channel.CreateUnbounded<Action>();
        internal void Enqueue(Action callback) => _callbacks.Writer.TryWrite(callback);
        internal async Task ApplyAsync(EventPublishingBackend backend, PlayerEvent playerEvent)
        {
            backend.Publish(playerEvent);
            (await _callbacks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))();
        }
    }
}

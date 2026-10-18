using System.Threading.Channels;
using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelTerminalTests
{
    [Theory]
    [InlineData("play")]
    [InlineData("pause")]
    [InlineData("seek")]
    [InlineData("load")]
    [InlineData("track")]
    [InlineData("volume")]
    [InlineData("mute")]
    [InlineData("stop")]
    [InlineData("speed")]
    public async Task A_session_failure_should_survive_late_command_success_and_playback_events(string command)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Pending() { started.TrySetResult(); return completion.Task; }
        var backend = new EventPublishingBackend
        {
            PlayHandler = _ => Pending(), PauseHandler = _ => Pending(),
            SetPositionHandler = (_, _) => Pending(), LoadHandler = (_, _) => Pending(),
            AudioTrackHandler = (_, _) => Pending(), VolumeHandler = (_, _) => Pending(), MuteHandler = (_, _) => Pending(),
            StopHandler = _ => Pending(), SpeedHandler = (_, _) => Pending(),
        };
        var ui = new TestDispatcher();
        await using var vm = CreateViewModel(backend, ui);
        await vm.InitializeAsync();
        vm.State = PlaybackState.Initial with
        {
            IsStopped = false, IsSeekable = true, CurrentUrl = "current.mp4", PositionSeconds = 10, DurationSeconds = 120, IsPlaying = command == "pause",
        };
        vm.UrlText = "next.mp4";
        var original = new TrackInfo(1, "audio", null, "原音轨", true);
        var next = new TrackInfo(2, "audio", null, "下一音轨", false);
        vm.Tracks.Add(original);
        vm.Tracks.Add(next);
        var request = command switch
        {
            "play" or "pause" => vm.TogglePlayPauseCommand.ExecuteAsync(null),
            "seek" => vm.SeekToAsync(60),
            "load" => vm.OpenUrlCommand.ExecuteAsync(null),
            "track" => vm.SelectTrackCommand.ExecuteAsync(next),
            "volume" => vm.ChangeVolumeAsync(20),
            "stop" => vm.StopCommand.ExecuteAsync(null),
            "speed" => vm.ChangeSpeedAsync(2),
            _ => vm.ToggleMuteCommand.ExecuteAsync(null),
        };
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            backend.Publish(new BackendFaulted("会话已关闭", PlayerErrorKind.Session));
            await ui.RunNextAsync();
            var terminal = vm.State with { IsPlaying = false, AreControlsVisible = true };
            completion.TrySetResult();
            await request.WaitAsync(TimeSpan.FromSeconds(5));
            vm.State.Should().Be(terminal);
            vm.ErrorMessage.Should().Be("会话已关闭");
            vm.Tracks.Should().Equal(original, next);
            vm.RecentUrls.Should().BeEmpty();

            backend.Publish(new PlaybackStateChanged(terminal with { IsPlaying = true, PositionSeconds = 99 }));
            await ui.RunNextAsync();
            backend.Publish(new BufferingChanged(true));
            await ui.RunNextAsync();
            vm.State.Should().Be(terminal);
            vm.IsBuffering.Should().BeFalse();
            vm.ErrorMessage.Should().Be("会话已关闭");
        }
        finally { completion.TrySetResult(); }
    }

    [Theory]
    [InlineData(PlayerErrorKind.Loading)]
    [InlineData(PlayerErrorKind.Playback)]
    [InlineData(PlayerErrorKind.EventQueue)]
    public async Task Recoverable_failures_should_allow_a_new_load(PlayerErrorKind kind)
    {
        var loads = 0;
        var backend = new EventPublishingBackend { LoadHandler = (_, _) => { loads++; return Task.CompletedTask; } };
        var ui = new TestDispatcher();
        await using var vm = CreateViewModel(backend, ui);
        await vm.InitializeAsync();
        backend.Publish(new BackendFaulted("可重新加载", kind));
        await ui.RunNextAsync();
        vm.UrlText = "next.mp4";
        await vm.OpenUrlCommand.ExecuteAsync(null);
        loads.Should().Be(1);
        vm.State.CurrentUrl.Should().Be("next.mp4");
        vm.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_broken_event_stream_should_pause_playback_and_reject_new_commands(bool pauseFails)
    {
        var calls = 0;
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Call() { calls++; return Task.CompletedTask; }
        var backend = new EventPublishingBackend
        {
            PlayHandler = _ => Call(), LoadHandler = (_, _) => Call(), SetPositionHandler = (_, _) => Call(),
            AudioTrackHandler = (_, _) => Call(), VolumeHandler = (_, _) => Call(), MuteHandler = (_, _) => Call(),
            StopHandler = _ => Call(), SpeedHandler = (_, _) => Call(),
            PauseHandler = _ =>
            {
                paused.TrySetResult();
                return pauseFails ? Task.FromException(new InvalidOperationException("暂停失败")) : Task.CompletedTask;
            },
        };
        var ui = new TestDispatcher();
        await using var vm = CreateViewModel(backend, ui);
        await vm.InitializeAsync();
        vm.State = PlaybackState.Initial with { IsStopped = false, IsSeekable = true, CurrentUrl = "current.mp4", IsPlaying = true };
        backend.FailEventObservation(new InvalidOperationException("事件流中断"));
        await ui.RunNextAsync();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.State.IsPlaying.Should().BeFalse();
        vm.UrlText = "next.mp4";
        await vm.TogglePlayPauseCommand.ExecuteAsync(null);
        await vm.OpenUrlCommand.ExecuteAsync(null);
        await vm.SeekToAsync(60);
        await vm.SelectTrackCommand.ExecuteAsync(new TrackInfo(2, "audio", null, "音轨", false));
        await vm.ChangeVolumeAsync(20);
        await vm.ToggleMuteCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        await vm.ChangeSpeedAsync(2);
        calls.Should().Be(0);
        vm.ErrorMessage.Should().Be("事件流中断");
    }

    [Fact]
    public async Task A_rejected_ui_dispatch_should_pause_and_allow_backend_disposal()
    {
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend
        {
            PauseHandler = _ => { paused.TrySetResult(); return Task.CompletedTask; },
        };
        await using var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator());
        vm.SetUiDispatcher(_ => throw new InvalidOperationException("UI 已关闭"));
        await vm.InitializeAsync();
        backend.Publish(new BufferingChanged(true));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        backend.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public async Task Initialization_failure_should_reject_later_commands_and_keep_the_original_error()
    {
        var loads = 0;
        var backend = new EventPublishingBackend
        {
            InitializationCompletion = Task.FromException(new InvalidOperationException("原生依赖无法加载")),
            LoadHandler = (_, _) => { loads++; return Task.CompletedTask; },
        };
        await using var vm = CreateViewModel(backend, new TestDispatcher());
        await vm.InitializeAsync();
        vm.UrlText = "next.mp4";
        await vm.OpenUrlCommand.ExecuteAsync(null);
        await vm.ChangeVolumeAsync(20);
        loads.Should().Be(0);
        vm.ErrorMessage.Should().Be("原生依赖无法加载");
        backend.ObserveCalls.Should().Be(0);
    }

    private static PlayerViewModel CreateViewModel(EventPublishingBackend backend, TestDispatcher ui)
    {
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator());
        vm.SetUiDispatcher(ui.Enqueue);
        return vm;
    }

    private sealed class TestDispatcher
    {
        private readonly Channel<Action> _callbacks = Channel.CreateUnbounded<Action>();
        internal void Enqueue(Action callback) => _callbacks.Writer.TryWrite(callback);
        internal async Task RunNextAsync() => (await _callbacks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))();
    }
}

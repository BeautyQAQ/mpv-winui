using System.Threading.Channels;
using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelEventTests
{
    [Fact]
    public async Task Backend_events_should_only_apply_when_the_ui_dispatcher_runs()
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        var initial = PlaybackState.Initial with { IsPlaying = true, AreControlsVisible = false };
        vm.State = initial;
        await vm.InitializeAsync();

        var track = new TrackInfo(1, "audio", "zho", "中文音轨", true);
        backend.Publish(new PlaybackStateChanged(initial with { PositionSeconds = 18, Volume = 42 }));
        backend.Publish(new TracksChanged([track]));
        backend.Publish(new BackendFaulted("媒体读取失败"));
        backend.Publish(new EndReached());
        var callbacks = await dispatcher.TakeAsync(4);

        vm.State.Should().Be(initial, "后端事件在 UI 回调执行前不能修改可绑定状态");
        vm.Tracks.Should().BeEmpty();
        vm.ErrorMessage.Should().BeNull();

        callbacks[0]();
        vm.State.PositionSeconds.Should().Be(18);
        vm.State.Volume.Should().Be(42);
        vm.State.IsPlaying.Should().BeTrue();
        callbacks[1]();
        vm.Tracks.Should().Equal(track);
        callbacks[2]();
        vm.ErrorMessage.Should().Be("媒体读取失败");
        vm.State.AreControlsVisible.Should().BeTrue();
        callbacks[3]();
        vm.State.IsPlaying.Should().BeFalse();
    }

    [Theory]
    [InlineData(OverlayKind.Osd)]
    [InlineData(OverlayKind.Tracks)]
    [InlineData(OverlayKind.InfoPanel)]
    public async Task Playback_updates_should_preserve_the_overlay_opened_while_an_event_is_queued(
        OverlayKind overlay)
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        await vm.InitializeAsync();
        backend.Publish(new PlaybackStateChanged(PlaybackState.Initial with
        {
            CurrentUrl = "http://127.0.0.1/sample.mp4",
            IsPlaying = true,
            PositionSeconds = 21,
            DurationSeconds = 54,
            Volume = 37,
            IsMuted = true,
        }));
        var callback = await dispatcher.TakeAsync();

        // 模拟事件排队后用户打开面板；合并时应读取执行时的 UI 状态。
        vm.State = vm.State with { CurrentOverlay = overlay, AreControlsVisible = true };
        callback();

        vm.State.CurrentOverlay.Should().Be(overlay);
        vm.State.AreControlsVisible.Should().BeTrue();
        vm.State.PositionSeconds.Should().Be(21);
        vm.State.DurationSeconds.Should().Be(54);
        vm.State.Volume.Should().Be(37);
        vm.State.IsMuted.Should().BeTrue();
        vm.State.CurrentUrl.Should().Be("http://127.0.0.1/sample.mp4");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Playback_updates_should_keep_controls_hidden_until_playback_pauses(
        bool isPlaying, bool expectedControlsVisible)
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        vm.State = PlaybackState.Initial with { IsPlaying = true, AreControlsVisible = false };
        await vm.InitializeAsync();

        backend.Publish(new PlaybackStateChanged(vm.State with
        {
            IsPlaying = isPlaying,
            AreControlsVisible = true,
        }));
        var callback = await dispatcher.TakeAsync();
        callback();

        vm.State.AreControlsVisible.Should().Be(expectedControlsVisible);
    }

    [Fact]
    public async Task Media_info_event_should_update_details_only_after_ui_dispatch()
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        await vm.InitializeAsync();
        var initialSummary = vm.InfoPanel.VideoSummary;

        backend.Publish(new MediaInfoChanged(new InfoPanelSnapshot(
            "h264", "aac", "SDR", "1280x720", "8-bit", "29.970", "forward=12s")));
        var callback = await dispatcher.TakeAsync();

        vm.InfoPanel.VideoSummary.Should().Be(initialSummary);
        callback();
        vm.InfoPanel.VideoSummary.Should().Contain("h264").And.Contain("1280x720")
            .And.Contain("8-bit").And.Contain("29.970");
        vm.InfoPanel.AudioSummary.Should().Contain("aac").And.Contain("forward=12s");
        vm.InfoPanel.HdrSummary.Should().Be("SDR");
    }

    [Fact]
    public async Task Dispose_should_cancel_event_observation_and_ignore_all_queued_ui_callbacks()
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        var initial = PlaybackState.Initial with
        {
            IsPlaying = true,
            PositionSeconds = 5,
            CurrentOverlay = OverlayKind.InfoPanel,
        };
        vm.State = initial;
        await vm.InitializeAsync();
        var initialVideoSummary = vm.InfoPanel.VideoSummary;
        var initialAudioSummary = vm.InfoPanel.AudioSummary;
        var initialHdrSummary = vm.InfoPanel.HdrSummary;

        backend.Publish(new PlaybackStateChanged(initial with { PositionSeconds = 48 }));
        backend.Publish(new TracksChanged([new TrackInfo(1, "audio", null, "音轨", true)]));
        backend.Publish(new MediaInfoChanged(new InfoPanelSnapshot(
            "h264", "aac", "SDR", "1280x720", "8-bit", "29.970", null)));
        backend.Publish(new BackendFaulted("延迟到达的错误"));
        backend.Publish(new EndReached());
        var callbacks = await dispatcher.TakeAsync(5);

        var disposal = vm.DisposeAsync().AsTask();
        foreach (var callback in callbacks)
            callback();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        backend.ObservedCancellationToken.IsCancellationRequested.Should().BeTrue();
        vm.State.Should().Be(initial);
        vm.Tracks.Should().BeEmpty();
        vm.InfoPanel.VideoSummary.Should().Be(initialVideoSummary);
        vm.InfoPanel.AudioSummary.Should().Be(initialAudioSummary);
        vm.InfoPanel.HdrSummary.Should().Be(initialHdrSummary);
        vm.ErrorMessage.Should().BeNull();
        await vm.DisposeAsync();
        backend.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public async Task Relative_seek_should_not_add_its_delta_twice_when_the_time_event_arrives_before_the_reply()
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        var initial = PlaybackState.Initial with
        {
            CurrentUrl = "http://127.0.0.1/sample.mp4",
            PositionSeconds = 20,
            DurationSeconds = 120,
        };
        vm.State = initial;
        await vm.InitializeAsync();
        backend.SeekHandler = async (deltaSeconds, _) =>
        {
            deltaSeconds.Should().Be(30);
            // 原生 time-pos 可能先于异步命令回复抵达 UI。
            backend.Publish(new PlaybackStateChanged(initial with { PositionSeconds = 50 }));
            var callback = await dispatcher.TakeAsync();
            callback();
            vm.State.PositionSeconds.Should().Be(50);
        };

        await vm.SeekForwardCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().BeNull();
        vm.State.PositionSeconds.Should().Be(50, "已收到的真实跳转位置不能再次加上相对偏移");
        vm.State.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_disposals_should_wait_for_initialization_and_never_start_event_observation()
    {
        var initializationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { InitializationCompletion = initializationCompletion.Task };
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        var initialization = vm.InitializeAsync();
        await backend.InitializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var firstDisposal = vm.DisposeAsync().AsTask();
        var secondDisposal = vm.DisposeAsync().AsTask();
        try
        {
            backend.DisposeCalls.Should().Be(0, "初始化正在使用后端时不能提前销毁它");
            firstDisposal.IsCompleted.Should().BeFalse();
            secondDisposal.IsCompleted.Should().BeFalse("每次关闭调用都必须等待资源释放结束");
        }
        finally
        {
            // 即使断言失败也释放受控初始化，避免测试清理永远等待。
            initializationCompletion.TrySetResult();
        }

        await Task.WhenAll(initialization, firstDisposal, secondDisposal).WaitAsync(TimeSpan.FromSeconds(5));

        backend.DisposeCalls.Should().Be(1);
        backend.ObserveCalls.Should().Be(0, "关闭开始后初始化完成也不能启动事件泵");
        vm.IsInitialized.Should().BeFalse();
        vm.ErrorMessage.Should().BeNull();
    }

    private static PlayerViewModel CreateViewModel(EventPublishingBackend backend, QueuedUiDispatcher dispatcher)
    {
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator());
        vm.SetUiDispatcher(dispatcher.Enqueue);
        return vm;
    }

    private sealed class QueuedUiDispatcher
    {
        private readonly Channel<Action> _callbacks = Channel.CreateUnbounded<Action>();

        public void Enqueue(Action callback) => _callbacks.Writer.TryWrite(callback);

        public async Task<Action> TakeAsync() =>
            await _callbacks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public async Task<Action[]> TakeAsync(int count)
        {
            var callbacks = new Action[count];
            for (var index = 0; index < count; index++)
                callbacks[index] = await TakeAsync();
            return callbacks;
        }
    }
}

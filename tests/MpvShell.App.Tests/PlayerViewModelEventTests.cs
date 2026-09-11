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
    public async Task Play_reply_after_fatal_rendering_failure_should_not_restore_playing_state()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend
        {
            PlayHandler = _ => { started.TrySetResult(); return completion.Task; },
        };
        await using var vm = CreateViewModel(backend, new QueuedUiDispatcher());
        await vm.InitializeAsync();
        vm.State = PlaybackState.Initial with { CurrentUrl = "current.mp4", IsPlaying = false };

        var play = vm.TogglePlayPauseCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.ReportRenderingFailure("渲染不可恢复，请重新打开播放器。");
            completion.TrySetResult();
            await play.WaitAsync(TimeSpan.FromSeconds(5));
            await vm.ChangeVolumeAsync(20);

            vm.HasRenderingFailure.Should().BeTrue();
            vm.State.IsPlaying.Should().BeFalse("命令发出后的终态故障优先于迟到的播放成功回复");
            vm.State.Volume.Should().Be(20);
            vm.ErrorMessage.Should().Be("渲染不可恢复，请重新打开播放器。");
            vm.State.AreControlsVisible.Should().BeTrue();
        }
        finally { completion.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Seek_reply_after_fatal_rendering_failure_should_not_overwrite_terminal_state(bool relative)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Seek(double _, CancellationToken token) { started.TrySetResult(); return completion.Task; }
        var backend = new EventPublishingBackend { SeekHandler = Seek, SetPositionHandler = Seek };
        await using var vm = CreateViewModel(backend, new QueuedUiDispatcher());
        await vm.InitializeAsync();
        vm.State = PlaybackState.Initial with
        {
            CurrentUrl = "current.mp4", PositionSeconds = 10, DurationSeconds = 120, CurrentOverlay = OverlayKind.InfoPanel,
        };

        var seek = relative ? vm.SeekForwardCommand.ExecuteAsync(null) : vm.SeekToAsync(80);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.ReportRenderingFailure("渲染不可恢复");
            var terminalState = vm.State;
            completion.TrySetResult();
            await seek.WaitAsync(TimeSpan.FromSeconds(5));

            vm.State.Should().Be(terminalState);
            vm.ErrorMessage.Should().Be("渲染不可恢复");
        }
        finally { completion.TrySetResult(); }
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("sub")]
    public async Task Track_reply_after_fatal_rendering_failure_should_not_change_selection(string kind)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Select(int _, CancellationToken token) { started.TrySetResult(); return completion.Task; }
        var backend = new EventPublishingBackend { AudioTrackHandler = Select, SubtitleTrackHandler = Select };
        await using var vm = CreateViewModel(backend, new QueuedUiDispatcher());
        await vm.InitializeAsync();
        var original = new TrackInfo(1, kind, null, "原轨道", true);
        var next = new TrackInfo(2, kind, null, "另一轨道", false);
        vm.Tracks.Add(original);
        vm.Tracks.Add(next);
        vm.State = PlaybackState.Initial with { CurrentUrl = "current.mp4", CurrentOverlay = OverlayKind.Tracks };

        var selection = vm.SelectTrackCommand.ExecuteAsync(next);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.ReportRenderingFailure("渲染不可恢复");
            var terminalState = vm.State;
            completion.TrySetResult();
            await selection.WaitAsync(TimeSpan.FromSeconds(5));

            vm.Tracks.Should().Equal(original, next);
            vm.State.Should().Be(terminalState);
            vm.ErrorMessage.Should().Be("渲染不可恢复");
        }
        finally { completion.TrySetResult(); }
    }

    [Fact]
    public async Task Load_reply_after_fatal_rendering_failure_should_not_replace_the_displayed_media()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend
        {
            LoadHandler = (_, _) => { started.TrySetResult(); return completion.Task; },
        };
        await using var vm = CreateViewModel(backend, new QueuedUiDispatcher());
        await vm.InitializeAsync();
        vm.State = PlaybackState.Initial with { CurrentUrl = "current.mp4" };
        vm.UrlText = "next.mp4";
        var recentUrls = vm.RecentUrls.ToArray();

        var load = vm.OpenUrlCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.ReportRenderingFailure("渲染不可恢复");
            var terminalState = vm.State;
            completion.TrySetResult();
            await load.WaitAsync(TimeSpan.FromSeconds(5));

            vm.State.Should().Be(terminalState);
            vm.RecentUrls.Should().Equal(recentUrls);
            vm.ErrorMessage.Should().Be("渲染不可恢复");
        }
        finally { completion.TrySetResult(); }
    }

    [Fact]
    public async Task Fatal_rendering_failure_should_reject_new_play_load_seek_and_track_requests()
    {
        var playbackRequests = 0;
        Task Track(int _, CancellationToken token) { playbackRequests++; return Task.CompletedTask; }
        Task Seek(double _, CancellationToken token) { playbackRequests++; return Task.CompletedTask; }
        var backend = new EventPublishingBackend
        {
            PlayHandler = _ => { playbackRequests++; return Task.CompletedTask; },
            LoadHandler = (_, _) => { playbackRequests++; return Task.CompletedTask; },
            SeekHandler = Seek,
            SetPositionHandler = Seek,
            AudioTrackHandler = Track,
            SubtitleTrackHandler = Track,
        };
        await using var vm = CreateViewModel(backend, new QueuedUiDispatcher());
        await vm.InitializeAsync();
        vm.State = PlaybackState.Initial with { CurrentUrl = "current.mp4", PositionSeconds = 10, DurationSeconds = 120 };
        vm.UrlText = "next.mp4";
        vm.ReportRenderingFailure("渲染不可恢复");

        await vm.TogglePlayPauseCommand.ExecuteAsync(null);
        await vm.OpenUrlCommand.ExecuteAsync(null);
        await vm.SeekToAsync(60);
        await vm.SeekForwardCommand.ExecuteAsync(null);
        await vm.SeekBackwardCommand.ExecuteAsync(null);
        await vm.SelectTrackCommand.ExecuteAsync(new TrackInfo(2, "audio", null, "音轨", false));
        await vm.SelectTrackCommand.ExecuteAsync(new TrackInfo(2, "sub", null, "字幕", false));
        await vm.ChangeVolumeAsync(20);

        playbackRequests.Should().Be(0);
        vm.State.PositionSeconds.Should().Be(10);
        vm.State.Volume.Should().Be(20);
        vm.ErrorMessage.Should().Be("渲染不可恢复");
    }

    [Fact]
    public async Task Fatal_rendering_error_should_survive_commands_and_queued_playback_events()
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        await vm.InitializeAsync();
        vm.State = PlaybackState.Initial with { CurrentUrl = "current.mp4", IsPlaying = true };
        backend.Publish(new PlaybackStateChanged(vm.State));
        backend.Publish(new BufferingChanged(true));
        var queued = await dispatcher.TakeAsync(2);

        vm.ReportRenderingFailure("视频渲染失败，请重新打开播放器。");
        foreach (var callback in queued) callback();
        await vm.ChangeVolumeAsync(20);
        await vm.TogglePlayPauseCommand.ExecuteAsync(null);
        vm.UrlText = "next.mp4";
        await vm.OpenUrlCommand.ExecuteAsync(null);

        vm.HasRenderingFailure.Should().BeTrue();
        vm.ErrorMessage.Should().Be("视频渲染失败，请重新打开播放器。");
        vm.ErrorVisibility.Should().Be(Microsoft.UI.Xaml.Visibility.Visible);
        vm.State.IsPlaying.Should().BeFalse();
        vm.State.AreControlsVisible.Should().BeTrue();
        vm.State.CurrentUrl.Should().Be("current.mp4");
        vm.IsBuffering.Should().BeFalse();
    }

    [Fact]
    public async Task Buffering_events_should_show_and_clear_the_indicator_on_the_ui_dispatcher()
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        await vm.InitializeAsync();
        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        backend.Publish(new BufferingChanged(true));
        var bufferingStarted = await dispatcher.TakeAsync();
        vm.IsBuffering.Should().BeFalse("绑定状态只能由 UI 调度回调修改");
        changedProperties.Should().BeEmpty();
        bufferingStarted();
        vm.IsBuffering.Should().BeTrue();
        vm.BufferingVisibility.Should().Be(Microsoft.UI.Xaml.Visibility.Visible);
        changedProperties.Should().Equal(nameof(vm.IsBuffering), nameof(vm.BufferingVisibility));

        changedProperties.Clear();
        backend.Publish(new BufferingChanged(true));
        (await dispatcher.TakeAsync())();
        changedProperties.Should().BeEmpty("重复的缓冲事件不应刷新绑定");

        backend.Publish(new BufferingChanged(false));
        (await dispatcher.TakeAsync())();
        vm.IsBuffering.Should().BeFalse();
        vm.BufferingVisibility.Should().Be(Microsoft.UI.Xaml.Visibility.Collapsed);
        changedProperties.Should().Equal(nameof(vm.IsBuffering), nameof(vm.BufferingVisibility));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Playback_and_pause_updates_should_preserve_buffering_until_the_backend_clears_it(bool isPlaying)
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        await vm.InitializeAsync();
        backend.Publish(new BufferingChanged(true));
        (await dispatcher.TakeAsync())();

        backend.Publish(new PlaybackStateChanged(vm.State with { IsPlaying = isPlaying, PositionSeconds = 12 }));
        (await dispatcher.TakeAsync())();

        vm.State.IsPlaying.Should().Be(isPlaying);
        vm.State.PositionSeconds.Should().Be(12);
        vm.IsBuffering.Should().BeTrue("暂停和缓冲是独立状态，只有缓冲事件才能清除等待中的缓冲");
        vm.BufferingVisibility.Should().Be(Microsoft.UI.Xaml.Visibility.Visible);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Loading_new_media_should_reset_old_buffering_and_preserve_new_buffering_on_success(bool fails)
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        await vm.InitializeAsync();
        backend.Publish(new BufferingChanged(true));
        (await dispatcher.TakeAsync())();
        bool? wasBufferingAtLoadStart = null;
        backend.LoadHandler = async (_, _) =>
        {
            wasBufferingAtLoadStart = vm.IsBuffering;
            backend.Publish(new BufferingChanged(true));
            (await dispatcher.TakeAsync())();
            if (fails) throw new InvalidOperationException("打开媒体失败");
        };
        vm.UrlText = "http://127.0.0.1/next.mp4";

        await vm.OpenUrlCommand.ExecuteAsync(null);

        wasBufferingAtLoadStart.Should().BeFalse("新媒体开始加载前应清除旧文件的缓冲提示");
        vm.IsBuffering.Should().Be(!fails);
        vm.ErrorMessage.Should().Be(fails ? "打开媒体失败" : null);
    }

    [Fact]
    public async Task Event_observation_failure_should_clear_buffering_on_the_ui_dispatcher()
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        await vm.InitializeAsync();
        backend.Publish(new BufferingChanged(true));
        (await dispatcher.TakeAsync())();

        backend.FailEventObservation(new InvalidOperationException("事件流中断"));
        var failed = await dispatcher.TakeAsync();
        vm.IsBuffering.Should().BeTrue();
        vm.ErrorMessage.Should().BeNull();
        failed();

        vm.IsBuffering.Should().BeFalse();
        vm.ErrorMessage.Should().Be("事件流中断");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Terminal_events_should_clear_a_stale_buffering_indicator(bool faulted)
    {
        var backend = new EventPublishingBackend();
        var dispatcher = new QueuedUiDispatcher();
        await using var vm = CreateViewModel(backend, dispatcher);
        await vm.InitializeAsync();
        backend.Publish(new BufferingChanged(true));
        (await dispatcher.TakeAsync())();

        backend.Publish(faulted ? new BackendFaulted("读取失败") : new EndReached());
        (await dispatcher.TakeAsync())();

        vm.IsBuffering.Should().BeFalse();
    }

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
        backend.Publish(new BufferingChanged(true));
        var callbacks = await dispatcher.TakeAsync(6);

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
        vm.IsBuffering.Should().BeFalse();
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

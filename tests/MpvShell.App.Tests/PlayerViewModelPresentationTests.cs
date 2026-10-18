using FluentAssertions;
using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using System.Threading.Channels;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelPresentationTests
{
    [Fact]
    public async Task Initial_view_should_show_welcome_and_explain_unavailable_media_controls()
    {
        await using var vm = new PlayerViewModel(new EventPublishingBackend(), new());

        vm.WelcomeVisibility.Should().Be(Visibility.Visible);
        vm.StatusVisibility.Should().Be(Visibility.Collapsed);
        vm.CanTogglePlayback.Should().BeFalse();
        vm.CanStopPlayback.Should().BeFalse();
        vm.CanSelectTracks.Should().BeFalse();
        vm.PlaybackDisabledReason.Should().Contain("打开媒体");
        vm.SeekDisabledReason.Should().Contain("播放媒体");
        vm.CanAdjustVolume.Should().BeTrue();
        vm.CanChangeSpeed.Should().BeTrue();
    }

    [Fact]
    public async Task Loading_buffering_and_error_should_share_one_status_card_with_error_priority()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { LoadHandler = (_, _) => loaded.Task };
        var ui = new TestDispatcher();
        await using var vm = Create(backend, ui);
        await vm.InitializeAsync();
        vm.UrlText = "next.mp4";
        var open = vm.OpenUrlCommand.ExecuteAsync(null);
        try
        {
            await ui.ApplyAsync(backend, new BufferingChanged(true));
            vm.StatusTitle.Should().Be("正在打开媒体");
            vm.IsStatusBusy.Should().BeTrue();
            vm.StatusProgressVisibility.Should().Be(Visibility.Visible);
            vm.CanTogglePlayback.Should().BeFalse();
            vm.CanStopPlayback.Should().BeTrue();
            vm.CanSeek.Should().BeFalse();
            vm.SeekDisabledReason.Should().Contain("正在打开");

            vm.ReportError("无法连接媒体");
            vm.StatusTitle.Should().Be("播放遇到问题");
            vm.StatusMessage.Should().Be("无法连接媒体");
            vm.IsStatusBusy.Should().BeFalse();
            vm.StatusProgressVisibility.Should().Be(Visibility.Collapsed);
            vm.WelcomeVisibility.Should().Be(Visibility.Collapsed);
        }
        finally { loaded.TrySetResult(); await open; }

        vm.IsLoading.Should().BeFalse();
        vm.StatusTitle.Should().Be("正在缓冲");
        await ui.ApplyAsync(backend, new BufferingChanged(false));
        vm.StatusVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task Failed_load_should_replace_busy_status_with_the_failure()
    {
        var backend = new EventPublishingBackend { LoadHandler = (_, _) => Task.FromException(new InvalidOperationException("媒体不可用")) };
        await using var vm = new PlayerViewModel(backend, new()) { UrlText = "missing.mp4" };

        await vm.OpenUrlCommand.ExecuteAsync(null);

        vm.IsLoading.Should().BeFalse();
        vm.IsStatusBusy.Should().BeFalse();
        vm.StatusMessage.Should().Be("媒体不可用");
        vm.WelcomeVisibility.Should().Be(Visibility.Collapsed);
    }

    [Theory]
    [InlineData(OverlayKind.Osd)]
    [InlineData(OverlayKind.Tracks)]
    [InlineData(OverlayKind.InfoPanel)]
    public async Task Opening_from_a_panel_should_show_loading_and_failure_without_hiding_them_behind_the_panel(OverlayKind overlay)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { LoadHandler = (_, _) => loaded.Task };
        await using var vm = new PlayerViewModel(backend, new())
        {
            UrlText = "next.mp4", State = PlaybackState.Initial with { CurrentOverlay = overlay },
        };

        var open = vm.OpenUrlCommand.ExecuteAsync(null);
        try
        {
            vm.State.CurrentOverlay.Should().Be(OverlayKind.None);
            vm.StatusVisibility.Should().Be(Visibility.Visible);
            vm.StatusTitle.Should().Be("正在打开媒体");
            vm.State.AreControlsVisible.Should().BeTrue();
        }
        finally { loaded.TrySetException(new InvalidOperationException("媒体不可用")); await open; }

        vm.StatusVisibility.Should().Be(Visibility.Visible);
        vm.StatusMessage.Should().Be("媒体不可用");
    }

    [Fact]
    public async Task End_status_should_survive_late_buffering_and_clear_on_actual_playback()
    {
        var backend = new EventPublishingBackend();
        var ui = new TestDispatcher();
        await using var vm = Create(backend, ui);
        await vm.InitializeAsync();

        await ui.ApplyAsync(backend, new EndReached());
        await ui.ApplyAsync(backend, new BufferingChanged(true));
        vm.StatusTitle.Should().Be("播放结束");
        vm.IsStatusBusy.Should().BeFalse();
        vm.CanTogglePlayback.Should().BeTrue();

        await ui.ApplyAsync(backend, new BufferingChanged(false));
        await ui.ApplyAsync(backend, new PlaybackStateChanged(vm.State with { IsPlaying = true, PositionSeconds = 0 }));
        vm.StatusVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task An_old_end_event_during_loading_should_not_leave_a_finished_status_on_the_new_media()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { LoadHandler = (_, _) => loaded.Task };
        var ui = new TestDispatcher();
        await using var vm = Create(backend, ui);
        await vm.InitializeAsync();
        vm.UrlText = "next.mp4";
        var open = vm.OpenUrlCommand.ExecuteAsync(null);
        try
        {
            await ui.ApplyAsync(backend, new EndReached());
            vm.StatusTitle.Should().Be("正在打开媒体");
        }
        finally { loaded.TrySetResult(); await open; }

        vm.StatusVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task Stopped_status_should_offer_replay_and_disable_media_only_controls()
    {
        await using var vm = new PlayerViewModel(new EventPublishingBackend(), new())
        {
            State = PlaybackState.Initial with { CurrentUrl = "current.mp4" },
        };

        vm.StatusTitle.Should().Be("已停止");
        vm.CanTogglePlayback.Should().BeTrue();
        vm.CanStopPlayback.Should().BeFalse();
        vm.CanSelectTracks.Should().BeFalse();
        vm.SeekDisabledReason.Should().Contain("播放媒体");
    }

    [Fact]
    public async Task Terminal_failure_should_disable_controls_and_dominate_other_statuses()
    {
        var backend = new EventPublishingBackend();
        var ui = new TestDispatcher();
        await using var vm = Create(backend, ui);
        await vm.InitializeAsync();
        await ui.ApplyAsync(backend, new BufferingChanged(true));

        vm.ReportRenderingFailure("请重新打开播放器");

        vm.StatusTitle.Should().Be("播放器暂不可用");
        vm.StatusMessage.Should().Be("请重新打开播放器");
        vm.IsStatusBusy.Should().BeFalse();
        vm.CanTogglePlayback.Should().BeFalse();
        vm.CanStopPlayback.Should().BeFalse();
        vm.CanSelectTracks.Should().BeFalse();
        vm.CanChangeSpeed.Should().BeFalse();
        vm.CanAdjustVolume.Should().BeFalse();
        vm.PlaybackDisabledReason.Should().Contain("重新启动");
    }

    [Theory]
    [InlineData(false, 120, "不支持")]
    [InlineData(true, 0, "时长未知")]
    [InlineData(true, double.NaN, "时长未知")]
    public async Task Seek_disabled_reason_should_distinguish_capability_and_unknown_duration(bool seekable, double duration, string reason)
    {
        await using var vm = Create(new(), new());
        vm.State = vm.State with { IsSeekable = seekable, DurationSeconds = duration };
        vm.CanSeek.Should().BeFalse();
        vm.SeekDisabledReason.Should().Contain(reason);
    }

    [Fact]
    public async Task Track_options_should_group_families_keep_long_labels_and_have_one_subtitle_off_option()
    {
        await using var vm = Create(new(), new());
        var longTitle = new string('长', 300);
        vm.Tracks.Add(new(1, "audio", "eng", longTitle, true));
        vm.Tracks.Add(new(1, "sub", "zh-Hans", "中文 ASS", true));
        vm.Tracks.Add(new(2, "sub", null, null, false));
        vm.Tracks.Add(new(0, "sub", null, "关闭字幕", false));

        vm.AudioTrackOptions.Should().ContainSingle().Which.Label.Should().Be(longTitle);
        vm.AudioTrackOptions[0].Detail.Should().Be("英语 · 轨道 1");
        vm.AudioTrackOptions[0].AccessibleLabel.Should().EndWith("已选择");
        vm.SubtitleTrackOptions.Should().HaveCount(3);
        vm.SubtitleTrackOptions[0].Label.Should().Be("关闭字幕");
        vm.SubtitleTrackOptions[0].IsSelected.Should().BeFalse();
        vm.SubtitleTrackOptions[1].Detail.Should().Be("简体中文 · 轨道 1");
        vm.SubtitleTrackOptions[2].Label.Should().Be("字幕 2");
        vm.SubtitleTrackOptions[2].Detail.Should().Contain("语言未标注");
        vm.CanSelectTracks.Should().BeTrue();
    }

    [Fact]
    public async Task Track_option_selection_should_follow_backend_events_even_when_the_reply_arrives_later()
    {
        var selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int? requested = null;
        var backend = new EventPublishingBackend { SubtitleTrackHandler = (id, _) => { requested = id; return selected.Task; } };
        var ui = new TestDispatcher();
        await using var vm = Create(backend, ui);
        await vm.InitializeAsync();
        var audio = new TrackInfo(1, "audio", "en", "原音轨", true);
        var subtitle = new TrackInfo(1, "sub", "zh", "中文字幕", true);
        await ui.ApplyAsync(backend, new TracksChanged([audio, subtitle]));
        vm.State = vm.State with { CurrentOverlay = OverlayKind.Tracks };
        var subtitleOption = vm.SubtitleTrackOptions[1];
        var offOption = vm.SubtitleTrackOptions[0];
        var changes = 0;
        vm.SubtitleTrackOptions.CollectionChanged += (_, _) => changes++;

        var change = vm.SelectTrackOptionCommand.ExecuteAsync(vm.SubtitleTrackOptions[0]);
        try
        {
            requested.Should().Be(0);
            vm.SubtitleTrackOptions[0].IsSelected.Should().BeFalse("命令未产生轨道事件前不能改变选中标记");
            await ui.ApplyAsync(backend, new TracksChanged([audio, subtitle with { Selected = false }]));
            vm.SubtitleTrackOptions[0].IsSelected.Should().BeTrue();
        }
        finally { selected.TrySetResult(); await change; }

        vm.SubtitleTrackOptions[0].IsSelected.Should().BeTrue();
        vm.AudioTrackOptions[0].IsSelected.Should().BeTrue("关闭字幕不能影响重复 ID 的音轨");
        vm.SubtitleTrackOptions[0].Should().BeSameAs(offOption);
        vm.SubtitleTrackOptions[1].Should().BeSameAs(subtitleOption);
        changes.Should().Be(0, "事实回传只更新选中标记，保留轨道行容器与焦点");
        vm.State.CurrentOverlay.Should().Be(OverlayKind.Tracks, "选择后保留面板，允许连续切换音轨与字幕");
    }

    [Fact]
    public async Task Position_updates_should_not_rebuild_track_options_or_move_panel_focus()
    {
        await using var vm = Create(new(), new());
        vm.Tracks.Add(new(1, "audio", "en", "音轨", true));
        var option = vm.AudioTrackOptions[0];
        var changes = 0;
        vm.AudioTrackOptions.CollectionChanged += (_, _) => changes++;

        vm.State = vm.State with { PositionSeconds = 42 };

        changes.Should().Be(0);
        vm.AudioTrackOptions[0].Should().BeSameAs(option);
    }

    [Theory]
    [InlineData(OverlayKind.Osd)]
    [InlineData(OverlayKind.Tracks)]
    [InlineData(OverlayKind.InfoPanel)]
    public async Task Open_panels_should_hide_status_and_welcome_without_losing_the_status(OverlayKind overlay)
    {
        await using var vm = new PlayerViewModel(new EventPublishingBackend(), new());
        vm.State = vm.State with { CurrentOverlay = overlay };
        vm.WelcomeVisibility.Should().Be(Visibility.Collapsed);

        vm.ReportError("媒体不可用");
        vm.StatusVisibility.Should().Be(Visibility.Collapsed);
        vm.State = vm.State with { CurrentOverlay = OverlayKind.None };
        vm.StatusVisibility.Should().Be(Visibility.Visible);
        vm.StatusMessage.Should().Be("媒体不可用");
    }

    [Fact]
    public async Task Keyboard_play_and_track_commands_should_not_reach_the_backend_during_a_load()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = 0;
        var backend = new EventPublishingBackend
        {
            LoadHandler = (_, _) => loaded.Task,
            PlayHandler = _ => { commands++; return Task.CompletedTask; },
            PauseHandler = _ => { commands++; return Task.CompletedTask; },
            AudioTrackHandler = (_, _) => { commands++; return Task.CompletedTask; },
        };
        await using var vm = Create(backend, new());
        vm.UrlText = "next.mp4";
        var open = vm.OpenUrlCommand.ExecuteAsync(null);
        try
        {
            await vm.TogglePlayPauseCommand.ExecuteAsync(null);
            await vm.SelectTrackCommand.ExecuteAsync(new TrackInfo(1, "audio", null, "音轨", false));
            commands.Should().Be(0);
        }
        finally { loaded.TrySetResult(); await open; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replay_from_stopped_or_eof_should_show_loading_and_wait_for_backend_state(bool ended)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { PlayHandler = _ => loaded.Task };
        var ui = new TestDispatcher();
        await using var vm = Create(backend, ui);
        await vm.InitializeAsync();
        if (ended) await ui.ApplyAsync(backend, new EndReached());
        else vm.State = vm.State with { IsStopped = true, IsPlaying = false };

        var replay = vm.TogglePlayPauseCommand.ExecuteAsync(null);
        try
        {
            vm.IsLoading.Should().BeTrue();
            vm.StatusTitle.Should().Be("正在打开媒体");
            vm.CanTogglePlayback.Should().BeFalse();
            vm.CanStopPlayback.Should().BeTrue();
            vm.CanSeek.Should().BeFalse();
            vm.State.IsPlaying.Should().BeFalse();
        }
        finally { loaded.TrySetResult(); await replay; }

        vm.IsLoading.Should().BeFalse();
        vm.State.IsPlaying.Should().BeFalse("重播回复不能替代播放状态事实");
        vm.StatusTitle.Should().NotBe("播放结束");
        await ui.ApplyAsync(backend, new PlaybackStateChanged(vm.State with { IsStopped = false, IsPlaying = true, PositionSeconds = 0 }));
        vm.StatusVisibility.Should().Be(Visibility.Collapsed);
    }

    [Theory]
    [InlineData(false, "success")]
    [InlineData(false, "failure")]
    [InlineData(false, "canceled")]
    [InlineData(true, "success")]
    [InlineData(true, "failure")]
    [InlineData(true, "canceled")]
    public async Task Stop_should_cancel_a_pending_replay_and_ignore_its_late_outcome(bool ended, string outcome)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken replayToken = default;
        var backend = new EventPublishingBackend { PlayHandler = token => { replayToken = token; return loaded.Task; } };
        var ui = new TestDispatcher();
        await using var vm = Create(backend, ui);
        await vm.InitializeAsync();
        if (ended) await ui.ApplyAsync(backend, new EndReached());
        else vm.State = vm.State with { IsStopped = true, IsPlaying = false };
        var replay = vm.TogglePlayPauseCommand.ExecuteAsync(null);

        await vm.StopCommand.ExecuteAsync(null);
        await ui.ApplyAsync(backend, new PlaybackStateChanged(vm.State with { IsStopped = true, IsPlaying = false, PositionSeconds = 0, DurationSeconds = 0 }));
        var stopped = vm.State;
        replayToken.IsCancellationRequested.Should().BeTrue();
        vm.IsLoading.Should().BeFalse();
        if (outcome == "failure") loaded.TrySetException(new InvalidOperationException("迟到重播失败"));
        else if (outcome == "canceled") loaded.TrySetCanceled();
        else loaded.TrySetResult();
        await replay;

        vm.State.Should().Be(stopped);
        vm.IsLoading.Should().BeFalse();
        vm.ErrorMessage.Should().BeNull();
        vm.StatusTitle.Should().Be("已停止");
    }

    [Fact]
    public async Task An_older_replay_completion_should_not_clear_a_new_open_status()
    {
        var replayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { PlayHandler = _ => replayed.Task, LoadHandler = (_, _) => opened.Task };
        await using var vm = Create(backend, new());
        vm.State = vm.State with { IsStopped = true, IsPlaying = false };
        var replay = vm.TogglePlayPauseCommand.ExecuteAsync(null);
        vm.UrlText = "next.mp4";
        var open = vm.OpenUrlCommand.ExecuteAsync(null);
        try
        {
            replayed.TrySetResult();
            await replay;
            vm.IsLoading.Should().BeTrue();
            vm.StatusTitle.Should().Be("正在打开媒体");
        }
        finally { replayed.TrySetResult(); opened.TrySetResult(); await Task.WhenAll(replay, open); }
    }

    private static PlayerViewModel Create(EventPublishingBackend backend, TestDispatcher dispatcher)
    {
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator())
        {
            State = PlaybackState.Initial with
            {
                CurrentUrl = "current.mp4", IsStopped = false, IsPlaying = true,
                IsSeekable = true, DurationSeconds = 120, PositionSeconds = 20,
            },
        };
        vm.SetUiDispatcher(dispatcher.Enqueue);
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

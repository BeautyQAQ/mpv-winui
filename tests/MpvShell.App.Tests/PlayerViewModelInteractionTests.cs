using FluentAssertions;
using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelInteractionTests
{
    [Fact]
    public async Task Hover_should_show_target_without_moving_playback_or_the_slider()
    {
        var calls = new List<double>();
        await using var vm = CreateViewModel(CreateBackend(calls));
        vm.PreviewTimeline(90);
        vm.TimelinePreviewText.Should().Be("01:30");
        vm.TimelinePreviewVisibility.Should().Be(Visibility.Visible);
        vm.TimelineValue.Should().Be(20);
        vm.State.PositionSeconds.Should().Be(20);
        calls.Should().BeEmpty();
        vm.ClearTimelineHover();
        vm.HasTimelinePreview.Should().BeFalse();
    }

    [Fact]
    public async Task Drag_should_ignore_late_position_updates_and_send_only_the_final_release_target()
    {
        var calls = new List<double>();
        await using var vm = CreateViewModel(CreateBackend(calls));
        vm.BeginTimelineScrub(40).Should().BeTrue();
        vm.UpdateTimelineScrub(60);
        vm.State = vm.State with { PositionSeconds = 23 };
        vm.UpdateTimelineScrub(80);
        vm.State.PositionSeconds.Should().Be(23);
        vm.TimelineValue.Should().Be(80);
        calls.Should().BeEmpty();
        await vm.CommitTimelineScrubAsync();
        calls.Should().Equal(80);
        vm.HasTimelinePreview.Should().BeFalse();
        vm.HasActiveInteraction.Should().BeFalse();
    }

    [Fact]
    public async Task Keyboard_pending_target_should_accumulate_and_submit_once_without_changing_actual_position()
    {
        var calls = new List<double>();
        var clock = new InteractionTestClock();
        await using var vm = CreateViewModel(CreateBackend(calls), clock);
        vm.QueueTimelineSeek(vm.TimelineValue + 5);
        vm.QueueTimelineSeek(vm.TimelineValue + 5);
        vm.QueueTimelineSeek(vm.TimelineValue + 5);
        vm.State.PositionSeconds.Should().Be(20);
        vm.TimelineValue.Should().Be(35);
        clock.Advance(179);
        await vm.ProcessInteractionTickAsync();
        calls.Should().BeEmpty();
        clock.Advance(1);
        await Task.WhenAll(vm.ProcessInteractionTickAsync(), vm.ProcessInteractionTickAsync());
        calls.Should().Equal(35);
    }

    [Fact]
    public async Task Volume_preview_should_survive_old_state_events_until_debounced_submission()
    {
        var calls = new List<int>();
        var backend = new EventPublishingBackend { VolumeHandler = (value, _) => { calls.Add(value); return Task.CompletedTask; } };
        var clock = new InteractionTestClock();
        await using var vm = CreateViewModel(backend, clock);
        vm.QueueVolumeChange(70);
        vm.State = vm.State with { Volume = 45 };
        vm.VolumeValue.Should().Be(70);
        vm.State.Volume.Should().Be(45);
        clock.Advance(100);
        await vm.ProcessInteractionTickAsync();
        calls.Should().Equal(70);
        vm.VolumeValue.Should().Be(70);
        vm.FeedbackText.Should().Be("音量 70%");
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("stop")]
    [InlineData("load")]
    [InlineData("overlay")]
    [InlineData("unseekable")]
    [InlineData("terminal")]
    [InlineData("dispose")]
    public async Task Lifecycle_changes_should_clear_pending_inputs_before_they_can_submit(string change)
    {
        var calls = new List<double>();
        var volumeCalls = 0;
        var backend = CreateBackend(calls);
        backend.VolumeHandler = (_, _) => { volumeCalls++; return Task.CompletedTask; };
        var clock = new InteractionTestClock();
        await using var vm = CreateViewModel(backend, clock);
        vm.QueueTimelineSeek(80);
        vm.QueueVolumeChange(70);
        await ApplyChangeAsync(vm, change);
        clock.Advance(1000);
        await vm.ProcessInteractionTickAsync();
        calls.Should().BeEmpty();
        volumeCalls.Should().Be(0);
        vm.HasActiveInteraction.Should().BeFalse();
        vm.HasTimelinePreview.Should().BeFalse();
        vm.FeedbackText.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Focus_cancellation_should_suppress_a_submitted_seek_reply_without_reverting_native_events(bool fails)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { SetPositionHandler = (_, _) => completion.Task };
        await using var vm = CreateViewModel(backend);
        vm.BeginTimelineScrub(80);
        var request = vm.CommitTimelineScrubAsync();
        vm.FeedbackText.Should().Be("正在跳转…");
        vm.CancelInputInteractions();
        vm.State = vm.State with { PositionSeconds = 78 };
        if (fails) completion.SetException(new InvalidOperationException("旧跳转失败"));
        else completion.SetResult();
        await request;
        vm.FeedbackText.Should().BeEmpty();
        vm.ErrorMessage.Should().BeNull();
        vm.State.PositionSeconds.Should().Be(78);
    }

    [Fact]
    public async Task A_new_scrub_should_take_priority_over_an_older_submitted_seek_reply()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { SetPositionHandler = (_, _) => completion.Task };
        await using var vm = CreateViewModel(backend);
        var request = vm.SeekToAsync(70);
        vm.BeginTimelineScrub(40);
        completion.SetResult();
        await request;
        vm.TimelineValue.Should().Be(40);
        vm.State.PositionSeconds.Should().Be(20);
        vm.FeedbackText.Should().BeEmpty();
        vm.CancelTimelineScrub();
        vm.TimelineValue.Should().Be(20);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Feedback_should_wait_for_reply_and_disappear_after_success_or_failure(bool fails)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { SetPositionHandler = (_, _) => completion.Task };
        var clock = new InteractionTestClock();
        await using var vm = CreateViewModel(backend, clock);
        vm.BeginTimelineScrub(80);
        var request = vm.CommitTimelineScrubAsync();
        vm.FeedbackText.Should().Be("正在跳转…");
        vm.State.PositionSeconds.Should().Be(20);
        if (fails) completion.SetException(new InvalidOperationException("内核拒绝跳转"));
        else completion.SetResult();
        await request;
        vm.FeedbackText.Should().Be(fails ? "跳转失败" : "跳转至 01:20");
        vm.State.PositionSeconds.Should().Be(fails ? 20 : 80);
        clock.Advance(1500);
        await vm.ProcessInteractionTickAsync();
        vm.FeedbackVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task Cancelled_volume_completion_should_not_restore_feedback_or_a_stale_volume()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend { VolumeHandler = (_, _) => completion.Task };
        var clock = new InteractionTestClock();
        await using var vm = CreateViewModel(backend, clock);
        vm.QueueVolumeChange(80);
        clock.Advance(100);
        var request = vm.ProcessInteractionTickAsync();
        vm.CancelInputInteractions();
        completion.SetResult();
        await request;
        vm.State.Volume.Should().Be(50);
        vm.FeedbackText.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_should_clear_pending_feedback_for_seek_and_volume(bool volume)
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new EventPublishingBackend
        {
            SetPositionHandler = (_, token) => Task.Delay(Timeout.Infinite, token),
            VolumeHandler = (_, token) => Task.Delay(Timeout.Infinite, token),
        };
        await using var vm = CreateViewModel(backend);
        vm.BeginVideoGesture();
        vm.UpdateVideoGesture(volume ? 0 : 80, volume ? -80 : 0);
        var request = vm.CommitVideoGestureAsync(cancellation.Token);
        vm.FeedbackText.Should().StartWith("正在");
        cancellation.Cancel();
        await request;
        vm.FeedbackVisibility.Should().Be(Visibility.Collapsed);
        vm.ErrorMessage.Should().BeNull();
        vm.State.PositionSeconds.Should().Be(20);
        vm.State.Volume.Should().Be(50);
    }

    [Fact]
    public async Task Cancelling_an_older_request_should_not_hide_feedback_from_a_newer_success()
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new EventPublishingBackend { SetPositionHandler = (_, token) => Task.Delay(Timeout.Infinite, token) };
        await using var vm = CreateViewModel(backend);
        vm.BeginTimelineScrub(60);
        var old = vm.CommitTimelineScrubAsync(cancellation.Token);
        await vm.ChangeVolumeAsync(70);
        cancellation.Cancel();
        await old;
        vm.FeedbackText.Should().Be("音量 70%");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disposal_should_clear_interactions_without_notifying_closed_ui_even_when_replies_arrive_late(bool volume, bool fails)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new EventPublishingBackend
        {
            SetPositionHandler = (_, _) => completion.Task,
            VolumeHandler = (_, _) => completion.Task,
        };
        await using var vm = CreateViewModel(backend);
        vm.BeginVideoGesture();
        vm.UpdateVideoGesture(volume ? 0 : 80, volume ? -80 : 0);
        var request = vm.CommitVideoGestureAsync();
        var changes = 0;
        vm.PropertyChanged += (_, _) => changes++;
        await vm.DisposeAsync();
        if (fails) completion.SetException(new InvalidOperationException("关闭后到达的回复"));
        else completion.SetResult();
        await request;
        await vm.ProcessInteractionTickAsync();
        vm.CancelInputInteractions();
        changes.Should().Be(0, "关闭仅清理内部交互状态，不能继续唤醒绑定控件");
        vm.HasActiveInteraction.Should().BeFalse();
        vm.HasTimelinePreview.Should().BeFalse();
        vm.FeedbackText.Should().BeEmpty();
        vm.State.PositionSeconds.Should().Be(20);
        vm.State.Volume.Should().Be(50);
    }

    [Fact]
    public async Task Synthetic_gesture_should_lock_axis_and_submit_only_on_release()
    {
        var calls = new List<double>();
        await using var vm = CreateViewModel(CreateBackend(calls));
        vm.BeginVideoGesture().Should().BeTrue();
        vm.UpdateVideoGesture(80, 0);
        vm.UpdateVideoGesture(160, 300);
        vm.TimelineValue.Should().Be(40);
        vm.State.PositionSeconds.Should().Be(20);
        calls.Should().BeEmpty();
        await vm.CommitVideoGestureAsync();
        calls.Should().Equal(40);
    }

    [Fact]
    public async Task Vertical_gesture_should_preview_volume_then_report_the_confirmed_value()
    {
        var volume = 0;
        var backend = new EventPublishingBackend { VolumeHandler = (value, _) => { volume = value; return Task.CompletedTask; } };
        await using var vm = CreateViewModel(backend);
        vm.BeginVideoGesture();
        vm.UpdateVideoGesture(0, -120);
        vm.VolumeValue.Should().Be(80);
        vm.FeedbackText.Should().Be("音量预览 80%");
        vm.State.Volume.Should().Be(50);
        await vm.CommitVideoGestureAsync();
        volume.Should().Be(80);
        vm.FeedbackText.Should().Be("音量 80%");
    }

    [Fact]
    public async Task Hover_and_active_interaction_should_suspend_auto_hide()
    {
        await using var vm = CreateViewModel(new EventPublishingBackend());
        vm.PreviewTimeline(30);
        vm.OnIdleTimeout();
        vm.State.AreControlsVisible.Should().BeTrue();
        vm.ClearTimelineHover();
        vm.BeginVideoGesture();
        vm.OnIdleTimeout();
        vm.State.AreControlsVisible.Should().BeTrue();
        vm.CancelInputInteractions();
        vm.OnIdleTimeout();
        vm.State.AreControlsVisible.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(double.NaN, true)]
    [InlineData(double.PositiveInfinity, true)]
    [InlineData(120, false)]
    public async Task Unknown_duration_and_nonseekable_media_should_not_submit_absolute_relative_or_gesture_seek(double duration, bool seekable)
    {
        var calls = new List<double>();
        await using var vm = CreateViewModel(CreateBackend(calls));
        vm.State = vm.State with { DurationSeconds = duration, IsSeekable = seekable };
        vm.CanSeek.Should().BeFalse();
        vm.BeginTimelineScrub(40).Should().BeFalse();
        vm.QueueTimelineSeek(40);
        await vm.SeekToAsync(40);
        await vm.SeekForwardCommand.ExecuteAsync(null);
        await vm.HandleDragAsync(80, 0);
        calls.Should().BeEmpty();
        vm.TimelineValue.Should().Be(double.IsFinite(duration) && duration > 0 ? 20 : 0);
        vm.TimelineMaximum.Should().Be(double.IsFinite(duration) && duration > 0 ? duration : 1);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(120)]
    public async Task Nonseekable_media_and_unloaded_eof_should_keep_actual_progress_visible(double position)
    {
        var calls = new List<double>();
        await using var vm = CreateViewModel(CreateBackend(calls));
        vm.BeginTimelineScrub(80);
        vm.State = vm.State with { IsSeekable = false, IsPlaying = false, PositionSeconds = position };
        vm.CanSeek.Should().BeFalse();
        vm.HasTimelinePreview.Should().BeFalse();
        vm.TimelineMaximum.Should().Be(120);
        vm.TimelineValue.Should().Be(position);
        await vm.CommitTimelineScrubAsync();
        calls.Should().BeEmpty();

        vm.State = vm.State with { IsStopped = true, PositionSeconds = 0, DurationSeconds = 0 };
        vm.TimelineMaximum.Should().Be(1);
        vm.TimelineValue.Should().Be(0);
    }

    [Fact]
    public async Task Relative_seek_should_clamp_the_native_request_to_the_same_boundary_as_absolute_input()
    {
        var calls = new List<double>();
        await using var vm = CreateViewModel(CreateBackend(calls));
        vm.State = vm.State with { PositionSeconds = 115 };
        await vm.SeekForwardCommand.ExecuteAsync(null);
        calls.Should().Equal(5);
        vm.State.PositionSeconds.Should().Be(120);
    }

    private static EventPublishingBackend CreateBackend(List<double> calls) => new()
    {
        SetPositionHandler = (value, _) => { calls.Add(value); return Task.CompletedTask; },
        SeekHandler = (value, _) => { calls.Add(value); return Task.CompletedTask; },
    };

    private static PlayerViewModel CreateViewModel(EventPublishingBackend backend, TimeProvider? clock = null) =>
        new(backend, new PlaybackInteractionCoordinator(clock))
        {
            State = PlaybackState.Initial with
            {
                CurrentUrl = "current.mp4", IsStopped = false, IsSeekable = true, IsPlaying = true,
                PositionSeconds = 20, DurationSeconds = 120, Volume = 50, AreControlsVisible = true,
            },
        };

    private static Task ApplyChangeAsync(PlayerViewModel vm, string change)
    {
        switch (change)
        {
            case "stop": return vm.StopCommand.ExecuteAsync(null);
            case "load":
                vm.UrlText = "current.mp4";
                return vm.OpenUrlCommand.ExecuteAsync(null);
            case "overlay": vm.ToggleInfoPanelCommand.Execute(null); break;
            case "unseekable": vm.State = vm.State with { IsSeekable = false }; break;
            case "terminal": vm.ReportRenderingFailure("渲染器已关闭"); break;
            case "dispose": return vm.DisposeAsync().AsTask();
            default: vm.CancelInputInteractions(); break;
        }
        return Task.CompletedTask;
    }
}

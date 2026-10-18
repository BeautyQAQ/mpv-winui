using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlaybackInteractionPreviewTests
{
    private static PlaybackState Media => PlaybackState.Initial with
    {
        IsStopped = false, IsSeekable = true, PositionSeconds = 20, DurationSeconds = 120, Volume = 50,
    };

    [Fact]
    public void Hover_should_only_preview_while_drag_takes_priority_over_hover_and_actual_position()
    {
        var coordinator = new PlaybackInteractionCoordinator();
        coordinator.PreviewTimeline(Media, 80);
        coordinator.TimelinePreview.Should().Be(80);
        coordinator.TimelineTarget.Should().BeNull();
        coordinator.BeginTimelineScrub(Media, 40).Should().BeTrue();
        coordinator.PreviewTimeline(Media, 90);
        coordinator.UpdateTimelineScrub(Media with { PositionSeconds = 10 }, 55);
        coordinator.TimelinePreview.Should().Be(55);
        coordinator.CommitTimelineScrub(Media)!.Value.Should().Be(55);
        coordinator.TimelinePreview.Should().BeNull();
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(30, 30)]
    [InlineData(180, 120)]
    public void Scrub_keyboard_and_synthetic_gesture_should_share_the_same_target_bounds(double target, double expected)
    {
        var clock = new InteractionTestClock();
        var coordinator = new PlaybackInteractionCoordinator(clock);
        coordinator.BeginTimelineScrub(Media, target);
        coordinator.CommitTimelineScrub(Media)!.Value.Should().Be(expected);
        coordinator.QueueTimelineSeek(Media, target);
        clock.Advance(180);
        coordinator.TakeDueRequests().Should().ContainSingle().Which.Value.Should().Be(expected);
        coordinator.BeginVideoGesture(Media);
        coordinator.UpdateVideoGesture(Media, (target - 20) * 8, 0);
        coordinator.CommitVideoGesture(Media)!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(OverlayKind.InfoPanel)]
    [InlineData(OverlayKind.Tracks)]
    [InlineData(OverlayKind.Osd)]
    public void Overlay_should_take_priority_over_timeline_and_video_inputs(OverlayKind overlay)
    {
        var coordinator = new PlaybackInteractionCoordinator();
        var state = Media with { CurrentOverlay = overlay };
        coordinator.BeginTimelineScrub(state, 50).Should().BeFalse();
        coordinator.BeginVideoGesture(state).Should().BeFalse();
        coordinator.QueueTimelineSeek(state, 50);
        coordinator.QueueVolumeChange(state, 80);
        coordinator.PreviewTimeline(state, 50);
        coordinator.HasActiveInteraction.Should().BeFalse();
        coordinator.TimelinePreview.Should().BeNull();
        coordinator.VolumePreview.Should().BeNull();
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(0, 40)]
    [InlineData(10, 10)]
    public void Gesture_below_or_at_threshold_should_not_submit(double deltaX, double deltaY)
    {
        var coordinator = new PlaybackInteractionCoordinator();
        coordinator.BeginVideoGesture(Media);
        coordinator.UpdateVideoGesture(Media, deltaX, deltaY);
        coordinator.CommitVideoGesture(Media).Should().BeNull();
        coordinator.FeedbackText.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Gesture_axis_should_stay_locked_after_threshold(bool horizontal)
    {
        var coordinator = new PlaybackInteractionCoordinator();
        coordinator.BeginVideoGesture(Media);
        coordinator.UpdateVideoGesture(Media, horizontal ? 80 : 0, horizontal ? 0 : -80);
        coordinator.UpdateVideoGesture(Media, horizontal ? 80 : 800, horizontal ? -800 : -80);
        var request = coordinator.CommitVideoGesture(Media)!;
        request.Kind.Should().Be(horizontal ? PlayerGesture.Seek : PlayerGesture.Volume);
        request.Value.Should().Be(horizontal ? 30 : 70);
    }

    [Fact]
    public void Repeated_keyboard_and_volume_inputs_should_submit_latest_values_only_after_their_own_delay()
    {
        var clock = new InteractionTestClock();
        var coordinator = new PlaybackInteractionCoordinator(clock);
        coordinator.QueueTimelineSeek(Media, 30);
        coordinator.QueueVolumeChange(Media, 60);
        clock.Advance(90);
        coordinator.QueueTimelineSeek(Media, 50);
        coordinator.QueueVolumeChange(Media, 80);
        clock.Advance(99);
        coordinator.TakeDueRequests().Should().BeEmpty();
        clock.Advance(1);
        var volume = coordinator.TakeDueRequests().Should().ContainSingle().Which;
        volume.Kind.Should().Be(PlayerGesture.Volume);
        volume.Value.Should().Be(80);
        clock.Advance(80);
        coordinator.TakeDueRequests().Should().ContainSingle().Which.Value.Should().Be(50);
        coordinator.TakeDueRequests().Should().BeEmpty();
    }

    [Fact]
    public void Cancellation_should_remove_pending_requests_and_ignore_late_success_or_failure()
    {
        var clock = new InteractionTestClock();
        var coordinator = new PlaybackInteractionCoordinator(clock);
        var submitted = coordinator.CreateRequest(PlayerGesture.Seek, 50);
        coordinator.MarkSubmitted(submitted);
        coordinator.QueueTimelineSeek(Media, 60);
        coordinator.QueueVolumeChange(Media, 70);
        coordinator.Cancel();
        clock.Advance(1000);
        coordinator.Complete(submitted, "旧请求失败");
        coordinator.TakeDueRequests().Should().BeEmpty();
        coordinator.HasActiveInteraction.Should().BeFalse();
        coordinator.FeedbackText.Should().BeEmpty();
    }

    [Fact]
    public void Only_the_latest_feedback_should_be_visible_until_its_deadline()
    {
        var clock = new InteractionTestClock();
        var coordinator = new PlaybackInteractionCoordinator(clock);
        var old = coordinator.CreateRequest(PlayerGesture.Seek, 50);
        var current = coordinator.CreateRequest(PlayerGesture.Volume, 70);
        coordinator.MarkSubmitted(current);
        coordinator.FeedbackText.Should().Be("正在调整音量…");
        coordinator.Complete(current, "音量 70%");
        coordinator.Complete(old, "跳转失败");
        clock.Advance(1499);
        coordinator.Tick();
        coordinator.FeedbackText.Should().Be("音量 70%");
        clock.Advance(1);
        coordinator.Tick();
        coordinator.FeedbackText.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-10, true)]
    [InlineData(double.NaN, true)]
    [InlineData(double.PositiveInfinity, true)]
    [InlineData(120, false)]
    public void Unknown_duration_or_nonseekable_media_should_disable_every_timeline_input(double duration, bool seekable)
    {
        var coordinator = new PlaybackInteractionCoordinator();
        var state = Media with { DurationSeconds = duration, IsSeekable = seekable };
        PlaybackInteractionCoordinator.CanSeek(state).Should().BeFalse();
        coordinator.BeginTimelineScrub(state, 40).Should().BeFalse();
        coordinator.PreviewTimeline(state, 40);
        coordinator.QueueTimelineSeek(state, 40);
        coordinator.BeginVideoGesture(state);
        coordinator.UpdateVideoGesture(state, 80, 0);
        coordinator.CommitVideoGesture(state).Should().BeNull();
        coordinator.TimelinePreview.Should().BeNull();
    }
}

internal sealed class InteractionTestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    internal void Advance(int milliseconds) => _now += TimeSpan.FromMilliseconds(milliseconds);
}

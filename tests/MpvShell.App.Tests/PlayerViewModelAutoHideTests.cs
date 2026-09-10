using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelAutoHideTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http://127.0.0.1/sample.mp4")]
    public async Task Idle_timeout_should_keep_controls_visible_without_media_or_while_paused(string? url)
    {
        await using var vm = CreateViewModel();
        vm.State = PlaybackState.Initial with { CurrentUrl = url, AreControlsVisible = true };

        vm.OnIdleTimeout();

        vm.State.AreControlsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Idle_timeout_should_hide_controls_during_playback()
    {
        await using var vm = CreateViewModel();
        vm.State = PlaybackState.Initial with
        {
            CurrentUrl = "http://127.0.0.1/sample.mp4",
            IsPlaying = true,
            AreControlsVisible = true,
        };

        vm.OnIdleTimeout();

        vm.State.AreControlsVisible.Should().BeFalse();
    }

    [Theory]
    [InlineData(OverlayKind.Osd)]
    [InlineData(OverlayKind.Tracks)]
    [InlineData(OverlayKind.InfoPanel)]
    public async Task Idle_timeout_should_keep_an_open_overlay_visible_during_playback(OverlayKind overlay)
    {
        await using var vm = CreateViewModel();
        vm.State = PlaybackState.Initial with
        {
            IsPlaying = true,
            AreControlsVisible = true,
            CurrentOverlay = overlay,
        };

        vm.OnIdleTimeout();

        vm.State.AreControlsVisible.Should().BeTrue();
        vm.State.CurrentOverlay.Should().Be(overlay);
    }

    [Fact]
    public async Task Idle_timeout_should_keep_controls_visible_when_a_playback_error_is_shown()
    {
        await using var vm = CreateViewModel();
        vm.State = PlaybackState.Initial with { IsPlaying = true };
        vm.ReportError("无法读取媒体");

        vm.OnIdleTimeout();

        vm.State.AreControlsVisible.Should().BeTrue();
        vm.ErrorMessage.Should().Be("无法读取媒体");
    }

    private static PlayerViewModel CreateViewModel() =>
        new(new EventPublishingBackend(), new PlaybackInteractionCoordinator());
}

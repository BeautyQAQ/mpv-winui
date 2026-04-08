using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlaybackInteractionCoordinatorTests
{
    [Fact]
    public void Show_controls_should_close_transient_overlay_and_make_controls_visible()
    {
        var coordinator = new PlaybackInteractionCoordinator();
        var state = PlaybackState.Initial with { CurrentOverlay = OverlayKind.InfoPanel };

        var next = coordinator.ShowControls(state);

        next.AreControlsVisible.Should().BeTrue();
        next.CurrentOverlay.Should().Be(OverlayKind.None);
    }
}

using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class AutoHideTests
{
    [Fact]
    public void Idle_timeout_should_hide_controls_when_no_overlay_is_open()
    {
        var coordinator = new PlaybackInteractionCoordinator();
        var state = PlaybackState.Initial with { AreControlsVisible = true };

        var next = coordinator.OnIdleTimeout(state);

        next.AreControlsVisible.Should().BeFalse();
    }
}

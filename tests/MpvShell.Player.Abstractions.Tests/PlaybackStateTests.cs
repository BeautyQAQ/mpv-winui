using FluentAssertions;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.Abstractions.Tests;

public sealed class PlaybackStateTests
{
    [Fact]
    public void Initial_state_should_match_v1_defaults()
    {
        var state = PlaybackState.Initial;

        state.IsPlaying.Should().BeFalse();
        state.IsStopped.Should().BeTrue();
        state.IsSeekable.Should().BeFalse();
        state.Speed.Should().Be(1);
        state.PositionSeconds.Should().Be(0);
        state.DurationSeconds.Should().Be(0);
        state.Volume.Should().Be(100);
        state.CurrentOverlay.Should().Be(OverlayKind.None);
        state.AreControlsVisible.Should().BeFalse();
    }
}

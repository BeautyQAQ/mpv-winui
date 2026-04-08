using FluentAssertions;
using MpvShell.App.Services;

namespace MpvShell.App.Tests;

public sealed class GestureDecisionEngineTests
{
    [Fact]
    public void Horizontal_drag_should_be_classified_as_seek()
    {
        var engine = new GestureDecisionEngine();

        var gesture = engine.Classify(deltaX: 120, deltaY: 10);

        gesture.Should().Be(PlayerGesture.Seek);
    }
}

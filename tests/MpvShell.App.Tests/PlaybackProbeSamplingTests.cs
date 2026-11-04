using FluentAssertions;
using MpvShell.App.Diagnostics;

namespace MpvShell.App.Tests;

public sealed class PlaybackProbeSamplingTests
{
    [Theory]
    [InlineData(12, 1.3)]
    [InlineData(3, 0.5)]
    [InlineData(30, 3)]
    public void Remaining_playback_should_allow_multiple_samples(double duration, double position)
    {
        var interval = PlaybackProbeSampling.GetInterval(duration, position);
        interval.Should().BePositive().And.BeLessThanOrEqualTo(5);
        (position + interval * 3).Should().BeLessThan(duration - 1);
    }

    [Theory]
    [InlineData(12, 11)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void Insufficient_or_unknown_remaining_time_should_fail(double duration, double position)
    {
        var action = () => PlaybackProbeSampling.GetInterval(duration, position);
        action.Should().Throw<InvalidOperationException>();
    }
}

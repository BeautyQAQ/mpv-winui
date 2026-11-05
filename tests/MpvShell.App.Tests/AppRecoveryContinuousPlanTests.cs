#if DEBUG
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MpvShell.App.Diagnostics;

namespace MpvShell.App.Tests;

public sealed class AppRecoveryContinuousPlanTests
{
    private static readonly string[] MediaPaths = [@"C:\media\first.mp4", @"C:\media\second.ts"];

    [Fact]
    public void A_minimal_plan_should_use_bounded_short_defaults_without_accessing_media()
    {
        var plan = AppRecoveryProbe.ParseContinuousPlan(JsonSerializer.Serialize(new { MediaPaths }));

        plan.MediaPaths.Should().Equal(MediaPaths);
        plan.Cycles.Should().Be(2);
        plan.PlaySeconds.Should().Be(1.5);
        plan.DurationSeconds.Should().Be(0);
    }

    [Theory]
    [InlineData(@"C:\media\first.mp4", @"C:\MEDIA\.\first.mp4")]
    [InlineData("first.mp4", @"C:\media\second.ts")]
    [InlineData("https://example.com/first.mp4", @"C:\media\second.ts")]
    [InlineData(@"\\server\share\first.mp4", @"C:\media\second.ts")]
    public void A_plan_should_reject_duplicate_relative_or_remote_paths(string first, string second)
    {
        var json = JsonSerializer.Serialize(new { MediaPaths = new[] { first, second } });

        var parse = () => AppRecoveryProbe.ParseContinuousPlan(json);

        parse.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"MediaPaths\":null}")]
    [InlineData("{\"MediaPaths\":[]}")]
    public void A_plan_should_require_at_least_two_media_paths(string json)
    {
        var parse = () => AppRecoveryProbe.ParseContinuousPlan(json);

        parse.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 1.5, 0)]
    [InlineData(10001, 1.5, 0)]
    [InlineData(2, 0.49, 0)]
    [InlineData(2, 30.01, 0)]
    [InlineData(2, 1.5, -1)]
    [InlineData(2, 1.5, 86401)]
    [InlineData(2, double.NaN, 0)]
    [InlineData(2, 1.5, double.PositiveInfinity)]
    public void A_plan_should_reject_unbounded_or_non_finite_execution_parameters(int cycles, double playSeconds, double durationSeconds)
    {
        var json = JsonSerializer.Serialize(new { MediaPaths, Cycles = cycles, PlaySeconds = playSeconds, DurationSeconds = durationSeconds },
            new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });

        var parse = () => AppRecoveryProbe.ParseContinuousPlan(json);

        parse.Should().Throw<Exception>().Where(error => error.GetType() == typeof(ArgumentException) || error.GetType() == typeof(JsonException));
    }
}
#endif

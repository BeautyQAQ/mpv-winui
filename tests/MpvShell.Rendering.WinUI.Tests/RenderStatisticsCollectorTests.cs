using FluentAssertions;

namespace MpvShell.Rendering.WinUI.Tests;

/// <summary>呈现统计只做算术，不需要 GPU；它是性能报告的数值来源，必须先证明计数与分位数可信。</summary>
public sealed class RenderStatisticsCollectorTests
{
    [Fact]
    public void Snapshot_should_report_exact_counts_and_percentiles()
    {
        var collector = new RenderStatisticsCollector();
        for (var index = 1; index <= 100; index++)
            collector.RecordPresented(renderMilliseconds: index, presentMilliseconds: 2, wakeToPresentMilliseconds: index % 2 == 0 ? index : null);
        collector.RecordSkipped();

        var snapshot = collector.Snapshot(reset: false);
        snapshot.PresentedFrames.Should().Be(100);
        snapshot.SkippedFrames.Should().Be(1);
        snapshot.Render.Count.Should().Be(100);
        snapshot.Render.AverageMilliseconds.Should().BeApproximately(50.5, 1e-9);
        snapshot.Render.MedianMilliseconds.Should().Be(50);
        snapshot.Render.P95Milliseconds.Should().Be(95);
        snapshot.Render.MaximumMilliseconds.Should().Be(100);
        snapshot.Present.AverageMilliseconds.Should().Be(2);
        snapshot.WakeToPresent.Count.Should().Be(50, "未知的唤醒延迟不能混入统计");
        snapshot.ElapsedSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Reset_should_start_a_new_window_without_losing_the_previous_snapshot()
    {
        var collector = new RenderStatisticsCollector();
        collector.RecordPresented(10, 1, 12);
        var first = collector.Snapshot(reset: true);
        collector.RecordPresented(20, 1, null);
        collector.RecordPresented(30, 1, null);
        var second = collector.Snapshot(reset: false);

        first.PresentedFrames.Should().Be(1);
        first.Render.MaximumMilliseconds.Should().Be(10);
        second.PresentedFrames.Should().Be(2);
        second.Render.AverageMilliseconds.Should().Be(25);
        second.WakeToPresent.Count.Should().Be(0);
    }

    [Fact]
    public void Accumulator_should_keep_exact_totals_after_sample_capacity_is_exhausted()
    {
        var accumulator = new FrameTimingAccumulator(capacity: 4);
        foreach (var value in new double[] { 1, 2, 3, 4, 100, 200 }) accumulator.Add(value);

        var summary = accumulator.Summarize();
        summary.Count.Should().Be(6);
        summary.SampledCount.Should().Be(4, "容量用尽后停止保留样本，但仍精确累计");
        summary.AverageMilliseconds.Should().BeApproximately(310.0 / 6, 1e-9);
        summary.MaximumMilliseconds.Should().Be(200);
        summary.P95Milliseconds.Should().Be(4, "分位数只能由已保留的样本估计");
    }

    [Fact]
    public void Accumulator_should_reject_negative_or_non_finite_durations()
    {
        var accumulator = new FrameTimingAccumulator(capacity: 8);
        var negative = () => accumulator.Add(-1);
        var nan = () => accumulator.Add(double.NaN);
        negative.Should().Throw<ArgumentOutOfRangeException>();
        nan.Should().Throw<ArgumentOutOfRangeException>();
        accumulator.Summarize().Should().Be(FrameTimingSummary.Empty);
    }

    [Theory]
    [InlineData(0.50, new double[] { 5 }, 5)]
    [InlineData(0.95, new double[] { 1, 2, 3, 4 }, 4)]
    [InlineData(0.50, new double[] { 1, 2, 3, 4 }, 2)]
    public void Percentile_should_use_nearest_rank_without_interpolation(double fraction, double[] sorted, double expected) =>
        FrameTimingAccumulator.Percentile(sorted, fraction).Should().Be(expected);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData(" no ", false)]
    [InlineData("FALSE", false)]
    [InlineData("off", false)]
    public void Debug_layer_request_should_only_be_disabled_by_explicit_values(string? value, bool expected) =>
        D3D11DeviceManager.IsDebugLayerRequested(value).Should().Be(expected);
}

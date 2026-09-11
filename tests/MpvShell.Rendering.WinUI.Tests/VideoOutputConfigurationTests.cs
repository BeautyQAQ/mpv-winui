using FluentAssertions;
using Vortice.DXGI;

namespace MpvShell.Rendering.WinUI.Tests;

public sealed class VideoOutputConfigurationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Sdr_source_or_disabled_hdr_should_use_sdr_output(bool sourceHdr, bool displayHdr)
    {
        var output = VideoOutputConfiguration.Select(sourceHdr, displayHdr, 1000);
        output.Should().Be(VideoOutputConfiguration.Sdr);
        output.Format.Should().Be(Format.B8G8R8A8_UNorm);
        output.ColorSpace.Should().Be(ColorSpaceType.RgbFullG22NoneP709);
    }

    [Theory]
    [InlineData(null, 1000)]
    [InlineData(0d, 1000)]
    [InlineData(double.NaN, 1000)]
    [InlineData(double.PositiveInfinity, 1000)]
    [InlineData(100d, 203)]
    [InlineData(600d, 600)]
    [InlineData(20000d, 10000)]
    public void Hdr_output_should_match_pq_format_and_sanitize_display_peak(double? reportedPeak, double expectedPeak)
    {
        var output = VideoOutputConfiguration.Select(true, true, reportedPeak);
        output.IsHdr.Should().BeTrue();
        output.Format.Should().Be(Format.R10G10B10A2_UNorm);
        output.ColorSpace.Should().Be(ColorSpaceType.RgbFullG2084NoneP2020);
        output.PeakLuminance.Should().Be(expectedPeak);
    }
}

using FluentAssertions;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class VideoDecodeDiagnosticsTests
{
    [Fact]
    public void Hardware_configuration_alone_should_not_claim_hardware_decoding()
    {
        var info = LibMpvBackend.BuildInfo(new Dictionary<string, object?> { ["hwdec"] = "d3d11va" });
        info.DecodeDiagnostics!.Mode.Should().Be(VideoDecodeMode.Unknown);
        info.DecodeDiagnostics.IsGpuResident.Should().BeFalse();
        info.DecodeDiagnostics.DecoderDroppedFrames.Should().BeNull();
        info.DecodeDiagnostics.PresentationDroppedFrames.Should().BeNull();
    }

    [Fact]
    public void Actual_d3d11_egl_frames_should_report_gpu_residency_and_precise_counters()
    {
        var info = LibMpvBackend.BuildInfo(new Dictionary<string, object?>
        {
            ["hwdec-current"] = "d3d11va", ["hwdec-interop"] = "d3d11-egl",
            ["video-params"] = new Dictionary<string, object?>
            {
                ["pixelformat"] = "d3d11", ["hw-pixelformat"] = "p010",
                ["w"] = 3840L, ["h"] = 2160L, ["gamma"] = "pq",
            },
            ["decoder-frame-drop-count"] = 4L, ["frame-drop-count"] = 9L,
        });
        info.DecodeDiagnostics!.Mode.Should().Be(VideoDecodeMode.D3D11);
        info.DecodeDiagnostics.IsGpuResident.Should().BeTrue();
        info.DecodeDiagnostics.HardwarePixelFormat.Should().Be("p010");
        info.DecodeDiagnostics.DecoderDroppedFrames.Should().Be(4);
        info.DecodeDiagnostics.PresentationDroppedFrames.Should().Be(9);
        info.DynamicRange.Should().Be(VideoDynamicRange.Pq);
        info.BitDepth.Should().Be("10 bit");
        info.HdrType.Should().NotContain("输出");
    }

    [Theory]
    [InlineData("yuv420p", "8 bit")]
    [InlineData("yuv420p10le", "10 bit")]
    [InlineData("nv12", "8 bit")]
    [InlineData("p010", "10 bit")]
    [InlineData("unknown-format", null)]
    public void Sample_depth_should_come_from_pixel_format_without_assuming_hdr_is_ten_bit(string format, string? depth)
    {
        var info = LibMpvBackend.BuildInfo(new Dictionary<string, object?>
        {
            ["video-params"] = new Dictionary<string, object?> { ["pixelformat"] = format, ["gamma"] = "pq" },
        });
        info.BitDepth.Should().Be(depth);
    }

    [Theory]
    [InlineData("no", VideoDecodeMode.Software)]
    [InlineData("d3d11va-copy", VideoDecodeMode.CopyBack)]
    [InlineData("nvdec-copy", VideoDecodeMode.CopyBack)]
    [InlineData("nvdec", VideoDecodeMode.OtherHardware)]
    [InlineData("d3d11va", VideoDecodeMode.D3D11)]
    public void Missing_interop_or_software_frames_should_never_claim_gpu_residency(string decoder, VideoDecodeMode expectedMode)
    {
        var diagnostics = LibMpvBackend.BuildInfo(new Dictionary<string, object?>
        {
            ["hwdec-current"] = decoder, ["decoder-frame-drop-count"] = -1L,
            ["frame-drop-count"] = 0L,
        }).DecodeDiagnostics!;
        diagnostics.Mode.Should().Be(expectedMode);
        diagnostics.IsGpuResident.Should().BeFalse();
        diagnostics.DecoderDroppedFrames.Should().BeNull();
        diagnostics.PresentationDroppedFrames.Should().Be(0);
    }
}

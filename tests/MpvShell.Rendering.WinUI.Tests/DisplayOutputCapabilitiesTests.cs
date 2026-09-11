using System.Runtime.InteropServices;
using FluentAssertions;
using MpvShell.Rendering.WinUI.Interop;

namespace MpvShell.Rendering.WinUI.Tests;

public sealed class DisplayOutputCapabilitiesTests
{
    [Theory]
    [InlineData(DisplayOutputColorKind.StandardDynamicRange, true, false)]
    [InlineData(DisplayOutputColorKind.WideColorGamut, true, false)]
    [InlineData(DisplayOutputColorKind.HighDynamicRange, true, true)]
    [InlineData(DisplayOutputColorKind.Unknown, true, false)]
    public void Hdr_output_should_follow_current_system_mode_instead_of_available_capability(
        DisplayOutputColorKind kind, bool supported, bool enabled)
    {
        var capabilities = DisplayOutputCapabilities.FromSystem("DISPLAY1", kind, supported, 203, 1000, 0, 400);

        capabilities.IsHdrSupported.Should().Be(supported);
        capabilities.IsHdrEnabled.Should().Be(enabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Missing_or_invalid_luminance_should_remain_unknown(double value)
    {
        var capabilities = DisplayOutputCapabilities.FromSystem("DISPLAY1",
            DisplayOutputColorKind.HighDynamicRange, true, value, value, value, value);

        capabilities.SdrWhiteLevelInNits.Should().BeNull();
        capabilities.PeakLuminanceInNits.Should().BeNull();
        capabilities.FullFrameLuminanceInNits.Should().BeNull();
        if (value == 0) capabilities.MinLuminanceInNits.Should().Be(0);
        else capabilities.MinLuminanceInNits.Should().BeNull();
    }

    [Fact]
    public void System_brightness_should_be_preserved_without_assuming_hdr_reference_white()
    {
        var capabilities = DisplayOutputCapabilities.FromSystem("DISPLAY2",
            DisplayOutputColorKind.HighDynamicRange, true, 287, 1499, 0, 799);

        capabilities.SdrWhiteLevelInNits.Should().Be(287);
        capabilities.PeakLuminanceInNits.Should().Be(1499);
        capabilities.MinLuminanceInNits.Should().Be(0);
        capabilities.FullFrameLuminanceInNits.Should().Be(799);
    }

    [Fact]
    public void Unknown_display_should_not_claim_hdr_output_or_default_measured_luminance()
    {
        var capabilities = DisplayOutputCapabilities.Unknown;

        capabilities.IsAvailable.Should().BeFalse();
        capabilities.IsHdrEnabled.Should().BeFalse();
        capabilities.IsHdrSupported.Should().BeFalse();
        capabilities.SdrWhiteLevelInNits.Should().BeNull();
        capabilities.PeakLuminanceInNits.Should().BeNull();
    }

    [Fact]
    public void Monitor_info_layout_should_match_unicode_windows_sdk()
    {
        Marshal.SizeOf<DisplayMonitorNative.MonitorInfoEx>().Should().Be(104);
        Marshal.OffsetOf<DisplayMonitorNative.MonitorInfoEx>(nameof(DisplayMonitorNative.MonitorInfoEx.Flags))
            .ToInt32().Should().Be(36);
        Marshal.OffsetOf<DisplayMonitorNative.MonitorInfoEx>(nameof(DisplayMonitorNative.MonitorInfoEx.DeviceName))
            .ToInt32().Should().Be(40);
    }
}

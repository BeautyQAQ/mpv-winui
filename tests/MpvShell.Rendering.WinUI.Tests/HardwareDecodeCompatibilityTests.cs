using FluentAssertions;

namespace MpvShell.Rendering.WinUI.Tests;

public sealed class HardwareDecodeCompatibilityTests
{
    [Theory]
    [InlineData(0x8086u, 0x0416u, true)]
    [InlineData(0x8086u, 0x0A16u, true)]
    [InlineData(0x8086u, 0x0D26u, true)]
    [InlineData(0x8086u, 0x1916u, false)]
    [InlineData(0x10DEu, 0x0416u, false)]
    [InlineData(0x1002u, 0x0416u, false)]
    public void Hevc_workaround_should_only_apply_to_legacy_Intel(uint vendorId, uint deviceId, bool expected)
        => D3D11DeviceManager.IsLegacyIntelHevcDevice(vendorId, deviceId).Should().Be(expected);
}

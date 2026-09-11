using MpvShell.Player.LibMpv;
using Vortice.DXGI;

namespace MpvShell.Rendering.WinUI;

/// <summary>将源视频与显示器的实际状态映射为同一套 mpv、EGL 和 DXGI 输出约定。</summary>
internal sealed record VideoOutputConfiguration(MpvVideoOutputMode Mode, double PeakLuminance)
{
    public static VideoOutputConfiguration Sdr { get; } = new(MpvVideoOutputMode.Sdr, 203);

    public bool IsHdr => Mode == MpvVideoOutputMode.Hdr10;
    public Format Format => IsHdr ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm;
    public ColorSpaceType ColorSpace => IsHdr
        ? ColorSpaceType.RgbFullG2084NoneP2020 : ColorSpaceType.RgbFullG22NoneP709;

    public static VideoOutputConfiguration Select(bool isHdrSource, bool isHdrEnabled, double? peakLuminance)
    {
        if (!isHdrSource || !isHdrEnabled) return Sdr;
        // EDID/驱动未提供亮度时采用明确的保守目标，不能把此值报告为实测显示器能力。
        var peak = peakLuminance is > 0 && double.IsFinite(peakLuminance.Value)
            ? Math.Clamp(peakLuminance.Value, 203, 10000) : 1000;
        return new(MpvVideoOutputMode.Hdr10, Math.Round(peak));
    }
}

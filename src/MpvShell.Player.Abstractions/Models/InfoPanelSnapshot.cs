namespace MpvShell.Player.Abstractions.Models;

public sealed record InfoPanelSnapshot(
    string? VideoCodec,
    string? AudioCodec,
    string? HdrType,
    string? Resolution,
    string? BitDepth,
    string? FrameRate,
    string? CacheState,
    VideoDecodeDiagnostics? DecodeDiagnostics = null,
    VideoDynamicRange DynamicRange = VideoDynamicRange.Unknown);

public enum VideoDynamicRange { Unknown, Sdr, Pq, Hlg }

public enum VideoDecodeMode { Unknown, Software, D3D11, CopyBack, OtherHardware }

/// <summary>当前解码器和帧统计的实际状态；未知计数与零丢帧严格区分。</summary>
public sealed record VideoDecodeDiagnostics(
    VideoDecodeMode Mode,
    string? Decoder,
    string? Interop,
    string? PixelFormat,
    string? HardwarePixelFormat,
    long? DecoderDroppedFrames,
    long? PresentationDroppedFrames)
{
    public bool IsGpuResident => Mode == VideoDecodeMode.D3D11 &&
        Interop == "d3d11-egl" && PixelFormat == "d3d11";
}

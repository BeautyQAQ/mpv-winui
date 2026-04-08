namespace MpvShell.Player.Abstractions.Models;

public sealed record InfoPanelSnapshot(
    string? VideoCodec,
    string? AudioCodec,
    string? HdrType,
    string? Resolution,
    string? BitDepth,
    string? FrameRate,
    string? CacheState);

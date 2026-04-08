namespace MpvShell.Player.Abstractions.Models;

public sealed record PlaybackState(
    string? CurrentUrl,
    bool IsPlaying,
    double PositionSeconds,
    double DurationSeconds,
    int Volume,
    bool IsMuted,
    bool AreControlsVisible,
    OverlayKind CurrentOverlay)
{
    public static PlaybackState Initial =>
        new(null, false, 0, 0, 100, false, false, OverlayKind.None);
}

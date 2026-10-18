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
    public double Speed { get; init; } = 1;
    public bool IsStopped { get; init; } = true;
    /// <summary>后端已确认当前媒体支持定位；未知能力或没有已加载媒体时为 false。</summary>
    public bool IsSeekable { get; init; }

    public static PlaybackState Initial =>
        new(null, false, 0, 0, 100, false, false, OverlayKind.None);
}

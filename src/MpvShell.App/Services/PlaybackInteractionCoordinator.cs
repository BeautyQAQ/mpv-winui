using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Services;

public sealed class PlaybackInteractionCoordinator
{
    public PlaybackState ShowControls(PlaybackState state) =>
        state with
        {
            AreControlsVisible = true,
            CurrentOverlay = OverlayKind.None,
        };

    public PlaybackState HideControls(PlaybackState state) =>
        state with
        {
            AreControlsVisible = false,
            CurrentOverlay = OverlayKind.None,
        };

    public PlaybackState ToggleOverlay(PlaybackState state, OverlayKind overlay) =>
        state.CurrentOverlay == overlay
            ? state with { CurrentOverlay = OverlayKind.None }
            : state with { CurrentOverlay = overlay, AreControlsVisible = true };

    public PlaybackState OnIdleTimeout(PlaybackState state) =>
        state.CurrentOverlay == OverlayKind.None
            ? state with { AreControlsVisible = false }
            : state;
}

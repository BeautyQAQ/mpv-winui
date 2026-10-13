using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.Abstractions.Events;

public abstract record PlayerEvent;

public sealed record PlaybackStateChanged(PlaybackState State) : PlayerEvent;

public sealed record TracksChanged(IReadOnlyList<TrackInfo> Tracks) : PlayerEvent;

public sealed record BackendFaulted(string Message, PlayerErrorKind Kind = PlayerErrorKind.Playback) : PlayerEvent
{
    public bool IsTerminal => Kind is PlayerErrorKind.Initialization or PlayerErrorKind.Session
        or PlayerErrorKind.EventStream or PlayerErrorKind.Rendering;
}

/// <summary>应用错误类别，不向表现层暴露原生错误码或属性名。</summary>
public enum PlayerErrorKind
{
    Playback,
    Loading,
    EventQueue,
    Initialization,
    Session,
    EventStream,
    Rendering,
}

public sealed record MediaInfoChanged(InfoPanelSnapshot Snapshot) : PlayerEvent;

public sealed record EndReached : PlayerEvent;

public sealed record BufferingChanged(bool IsBuffering) : PlayerEvent;

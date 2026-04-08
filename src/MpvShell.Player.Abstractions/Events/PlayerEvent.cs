using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.Abstractions.Events;

public abstract record PlayerEvent;

public sealed record PlaybackStateChanged(PlaybackState State) : PlayerEvent;

public sealed record TracksChanged(IReadOnlyList<TrackInfo> Tracks) : PlayerEvent;

public sealed record BackendFaulted(string Message) : PlayerEvent;

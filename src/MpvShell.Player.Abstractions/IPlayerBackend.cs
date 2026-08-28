using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.Abstractions;

public interface IPlayerBackend : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task LoadUrlAsync(string url, CancellationToken cancellationToken);
    Task PlayAsync(CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken);
    Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken);
    Task SetVolumeAsync(int volume, CancellationToken cancellationToken);
    Task SetMuteAsync(bool muted, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken);
    Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken);
    Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken);
    Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(CancellationToken cancellationToken);
}

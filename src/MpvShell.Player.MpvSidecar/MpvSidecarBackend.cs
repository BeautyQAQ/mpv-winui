using System.Runtime.CompilerServices;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.MpvSidecar;

public sealed class MpvSidecarBackend : IPlayerBackend
{
    private readonly MpvProcessManager _processManager = new();
    private readonly MpvJsonIpcClient _ipcClient = new();
    private PlaybackState _state = PlaybackState.Initial;

    public async Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken)
    {
        var pipeName = $"mpvshell-{Environment.ProcessId}";
        var options = new MpvLaunchOptions("mpv.exe", pipeName, hostHandle);

        _processManager.Start(options);
        await _ipcClient.ConnectAsync(pipeName, cancellationToken);
        await _ipcClient.SendAsync(MpvCommandFactory.Observe("pause", 1), cancellationToken);
        await _ipcClient.SendAsync(MpvCommandFactory.Observe("time-pos", 2), cancellationToken);
    }

    public Task LoadUrlAsync(string url, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.LoadUrl(url), cancellationToken);

    public Task PlayAsync(CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("pause", false), cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("pause", true), cancellationToken);

    public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SeekRelative(deltaSeconds), cancellationToken);

    public Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SeekAbsolute(absoluteSeconds), cancellationToken);

    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("volume", volume), cancellationToken);

    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("mute", muted), cancellationToken);

    public Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrackInfo>>(Array.Empty<TrackInfo>());

    public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("aid", trackId), cancellationToken);

    public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("sid", trackId), cancellationToken);

    public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new InfoPanelSnapshot(null, null, null, null, null, null, null));

    public async IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield return new PlaybackStateChanged(_state);
    }

    public async ValueTask DisposeAsync()
    {
        await _ipcClient.DisposeAsync();
    }
}

using System.Diagnostics;
using System.Runtime.CompilerServices;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.MpvSidecar;

public sealed class MpvSidecarBackend : IPlayerBackend
{
    private readonly MpvProcessManager _processManager = new();
    private readonly MpvJsonIpcClient _ipcClient = new();
    private Process? _mpvProcess;
    private PlaybackState _state = PlaybackState.Initial;

    public async Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken)
    {
        try
        {
            var pipeName = $"mpvshell-{Environment.ProcessId}";
            var options = new MpvLaunchOptions("mpv.exe", pipeName, hostHandle);

            _mpvProcess = _processManager.Start(options);
            await _ipcClient.ConnectAsync(pipeName, cancellationToken);
            await SendCommandAsync(MpvCommandFactory.Observe("pause", 1), cancellationToken);
            await SendCommandAsync(MpvCommandFactory.Observe("time-pos", 2), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("初始化 mpv 后端失败", ex);
        }
    }

    public Task LoadUrlAsync(string url, CancellationToken cancellationToken) =>
        SendCommandAsync(MpvCommandFactory.LoadUrl(url), cancellationToken);

    public Task PlayAsync(CancellationToken cancellationToken) =>
        SendCommandAsync(MpvCommandFactory.SetProperty("pause", false), cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken) =>
        SendCommandAsync(MpvCommandFactory.SetProperty("pause", true), cancellationToken);

    public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken) =>
        SendCommandAsync(MpvCommandFactory.SeekRelative(deltaSeconds), cancellationToken);

    public Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken) =>
        SendCommandAsync(MpvCommandFactory.SeekAbsolute(absoluteSeconds), cancellationToken);

    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) =>
        SendCommandAsync(MpvCommandFactory.SetProperty("volume", volume), cancellationToken);

    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken) =>
        SendCommandAsync(MpvCommandFactory.SetProperty("mute", muted), cancellationToken);

    public Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrackInfo>>(Array.Empty<TrackInfo>());

    public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) =>
        SendCommandAsync(MpvCommandFactory.SetProperty("aid", trackId), cancellationToken);

    public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) =>
        SendCommandAsync(MpvCommandFactory.SetProperty("sid", trackId), cancellationToken);

    public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new InfoPanelSnapshot(null, null, null, null, null, null, null));

    public async IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PlaybackStateChanged(_state);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_mpvProcess is { HasExited: true })
            {
                yield return new BackendFaulted("后端连接已断开");
                yield break;
            }

            Task delayTask;
            try
            {
                delayTask = Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            await delayTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _ipcClient.DisposeAsync();

        if (_mpvProcess is { HasExited: false })
        {
            try
            {
                _mpvProcess.Kill(entireProcessTree: true);
                _mpvProcess.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
            }
        }

        _mpvProcess?.Dispose();
        _mpvProcess = null;
    }

    private async Task SendCommandAsync(string commandJson, CancellationToken cancellationToken)
    {
        try
        {
            await _ipcClient.SendAsync(commandJson, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("无法连接到 mpv IPC", ex);
        }
    }
}

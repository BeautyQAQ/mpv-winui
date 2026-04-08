using FluentAssertions;
using System.Runtime.CompilerServices;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelInitializationTests
{
    [Fact]
    public async Task Initialize_should_forward_host_handle_to_backend_once()
    {
        var backend = new RecordingBackend();
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator());

        await vm.InitializeAsync((nint)1234);
        await vm.InitializeAsync((nint)5678);

        backend.InitializeCalls.Should().Be(1);
        backend.LastHostHandle.Should().Be((nint)1234);
    }

    private sealed class RecordingBackend : IPlayerBackend
    {
        public int InitializeCalls { get; private set; }

        public nint LastHostHandle { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken)
        {
            InitializeCalls++;
            LastHostHandle = hostHandle;
            return Task.CompletedTask;
        }

        public Task LoadUrlAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PlayAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetMuteAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrackInfo>>(Array.Empty<TrackInfo>());

        public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InfoPanelSnapshot(null, null, null, null, null, null, null));

        public async IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}

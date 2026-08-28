using FluentAssertions;
using System.Runtime.CompilerServices;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelTrackSelectionTests
{
    [Theory]
    [InlineData("audio", 7, 7, null)]
    [InlineData("sub", 9, null, 9)]
    public async Task Select_track_should_route_to_matching_backend_command(
        string kind,
        int trackId,
        int? expectedAudioTrackId,
        int? expectedSubtitleTrackId)
    {
        var backend = new RecordingBackend();
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator());
        var targetTrack = new TrackInfo(trackId, kind, "en", "Track", false);

        vm.Tracks.Add(targetTrack);

        await vm.SelectTrackCommand.ExecuteAsync(targetTrack);

        backend.LastAudioTrackId.Should().Be(expectedAudioTrackId);
        backend.LastSubtitleTrackId.Should().Be(expectedSubtitleTrackId);
        vm.Tracks.Should().ContainSingle(track => track.Id == trackId && track.Selected);
    }

    private sealed class RecordingBackend : IPlayerBackend
    {
        public int? LastAudioTrackId { get; private set; }

        public int? LastSubtitleTrackId { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LoadUrlAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PlayAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetMuteAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrackInfo>>(Array.Empty<TrackInfo>());

        public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken)
        {
            LastAudioTrackId = trackId;
            return Task.CompletedTask;
        }

        public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken)
        {
            LastSubtitleTrackId = trackId;
            return Task.CompletedTask;
        }

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

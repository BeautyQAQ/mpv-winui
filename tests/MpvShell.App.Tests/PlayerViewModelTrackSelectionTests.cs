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
    [InlineData("sub", 0, null, 0)]
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

    [Fact]
    public async Task Closing_subtitles_should_keep_an_audio_track_with_the_same_id_selected()
    {
        var backend = new RecordingBackend();
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator());
        vm.Tracks.Add(new TrackInfo(1, "audio", "eng", "音轨", true));
        vm.Tracks.Add(new TrackInfo(1, "sub", "eng", "SRT", true));
        vm.Tracks.Add(new TrackInfo(2, "sub", "zho", "ASS", false));
        var off = new TrackInfo(0, "sub", null, "关闭字幕", false);
        vm.Tracks.Add(off);

        await vm.SelectTrackCommand.ExecuteAsync(off);

        backend.LastSubtitleTrackId.Should().Be(0);
        backend.LastAudioTrackId.Should().BeNull();
        vm.Tracks.Where(track => track.Kind == "audio").Should().ContainSingle().Which.Selected.Should().BeTrue();
        vm.Tracks.Where(track => track.Kind == "sub" && track.Selected).Should().ContainSingle().Which.Id.Should().Be(0);
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

using FluentAssertions;
using System.Runtime.CompilerServices;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelPlaybackCommandTests
{
    [Fact]
    public async Task Toggle_play_pause_should_start_playback_when_currently_paused()
    {
        var backend = new RecordingBackend();
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator());

        await vm.TogglePlayPauseCommand.ExecuteAsync(null);

        backend.PlayCalls.Should().Be(1);
        backend.PauseCalls.Should().Be(0);
        vm.State.IsPlaying.Should().BeTrue();
        vm.State.AreControlsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Seek_forward_should_request_relative_seek_and_keep_controls_visible()
    {
        var backend = new RecordingBackend();
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator())
        {
            State = PlaybackState.Initial with { PositionSeconds = 12, DurationSeconds = 100 }
        };

        await vm.SeekForwardCommand.ExecuteAsync(null);

        backend.LastSeekDelta.Should().Be(30);
        vm.State.PositionSeconds.Should().Be(42);
        vm.State.AreControlsVisible.Should().BeTrue();
    }

    private sealed class RecordingBackend : IPlayerBackend
    {
        public int PlayCalls { get; private set; }

        public int PauseCalls { get; private set; }

        public double? LastSeekDelta { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LoadUrlAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PlayAsync(CancellationToken cancellationToken)
        {
            PlayCalls++;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            PauseCalls++;
            return Task.CompletedTask;
        }

        public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken)
        {
            LastSeekDelta = deltaSeconds;
            return Task.CompletedTask;
        }

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

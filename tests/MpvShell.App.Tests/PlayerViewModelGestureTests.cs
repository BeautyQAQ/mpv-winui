using FluentAssertions;
using System.Runtime.CompilerServices;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelGestureTests
{
    [Fact]
    public async Task Handle_drag_should_seek_for_horizontal_drag()
    {
        var backend = new RecordingBackend();
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator(), new GestureDecisionEngine())
        {
            State = PlaybackState.Initial with { PositionSeconds = 10, DurationSeconds = 120 }
        };

        await vm.HandleDragAsync(deltaX: 120, deltaY: 10);

        backend.LastSeekDelta.Should().NotBeNull();
        backend.LastSeekDelta.Should().BePositive();
        vm.State.PositionSeconds.Should().BeGreaterThan(10);
        vm.State.AreControlsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Seek_to_should_set_absolute_position_and_show_controls()
    {
        var backend = new RecordingBackend();
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator(), new GestureDecisionEngine())
        {
            State = PlaybackState.Initial with { DurationSeconds = 120 }
        };

        await vm.SeekToAsync(35);

        backend.LastAbsolutePosition.Should().Be(35);
        vm.State.PositionSeconds.Should().Be(35);
        vm.State.AreControlsVisible.Should().BeTrue();
    }

    private sealed class RecordingBackend : IPlayerBackend
    {
        public double? LastSeekDelta { get; private set; }

        public double? LastAbsolutePosition { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LoadUrlAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PlayAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken)
        {
            LastSeekDelta = deltaSeconds;
            return Task.CompletedTask;
        }

        public Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken)
        {
            LastAbsolutePosition = absoluteSeconds;
            return Task.CompletedTask;
        }

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

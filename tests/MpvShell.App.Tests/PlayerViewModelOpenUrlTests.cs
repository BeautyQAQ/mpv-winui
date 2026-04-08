using FluentAssertions;
using System.Runtime.CompilerServices;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelOpenUrlTests
{
    [Fact]
    public async Task Open_url_should_store_current_url_and_show_controls()
    {
        var vm = new PlayerViewModel(new FakeBackend(), new PlaybackInteractionCoordinator())
        {
            UrlText = "https://example.com/master.m3u8"
        };

        await vm.OpenUrlCommand.ExecuteAsync(null);

        vm.State.CurrentUrl.Should().Be("https://example.com/master.m3u8");
        vm.State.AreControlsVisible.Should().BeTrue();
    }

    private sealed class FakeBackend : IPlayerBackend
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken) => Task.CompletedTask;

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

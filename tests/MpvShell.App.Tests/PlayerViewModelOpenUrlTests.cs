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

    [Fact]
    public async Task Open_url_should_refresh_recent_urls_tracks_and_info_panel()
    {
        var backend = new FakeBackend
        {
            Tracks =
            [
                new TrackInfo(1, "audio", "ja", "Japanese 5.1", true),
                new TrackInfo(2, "sub", "zh", "中文字幕", false),
            ],
            Snapshot = new InfoPanelSnapshot("hevc", "eac3", "HDR10", "3840x2160", "10-bit", "23.976", "forward=8s"),
        };

        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator())
        {
            UrlText = " https://example.com/master.m3u8 "
        };

        await vm.OpenUrlCommand.ExecuteAsync(null);

        vm.RecentUrls.Should().ContainSingle().Which.Should().Be("https://example.com/master.m3u8");
        vm.Tracks.Should().HaveCount(2);
        vm.InfoPanel.VideoSummary.Should().Contain("hevc");
        vm.InfoPanel.HdrSummary.Should().Contain("HDR10");
    }

    private sealed class FakeBackend : IPlayerBackend
    {
        public IReadOnlyList<TrackInfo> Tracks { get; init; } = Array.Empty<TrackInfo>();

        public InfoPanelSnapshot Snapshot { get; init; } = new(null, null, null, null, null, null, null);

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
            Task.FromResult(Tracks);

        public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public async IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}

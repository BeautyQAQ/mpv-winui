using FluentAssertions;
using System.Runtime.CompilerServices;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class ErrorPresentationTests
{
    [Fact]
    public async Task Open_url_should_store_error_message_when_backend_throws()
    {
        var vm = new PlayerViewModel(new ThrowingBackend(), new PlaybackInteractionCoordinator())
        {
            UrlText = "https://broken.example/stream.m3u8"
        };

        await vm.OpenUrlCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("无法连接到 mpv IPC");
    }

    [Fact]
    public async Task Initialize_should_surface_backend_fault_event_to_error_message()
    {
        var vm = new PlayerViewModel(new FaultEventBackend(), new PlaybackInteractionCoordinator());

        await vm.InitializeAsync();

        await AssertEventuallyAsync(
            () => vm.ErrorMessage,
            message => message == "后端连接已断开",
            TimeSpan.FromSeconds(1));

        vm.ErrorMessage.Should().Be("后端连接已断开");
    }

    private static async Task AssertEventuallyAsync<T>(
        Func<T> valueFactory,
        Func<T, bool> predicate,
        TimeSpan timeout)
    {
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            if (predicate(valueFactory()))
            {
                return;
            }

            await Task.Delay(20);
        }

        predicate(valueFactory()).Should().BeTrue("expected condition to be satisfied before timeout");
    }

    private sealed class ThrowingBackend : IPlayerBackend
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LoadUrlAsync(string url, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("无法连接到 mpv IPC");

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

    private sealed class FaultEventBackend : IPlayerBackend
    {
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

        public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InfoPanelSnapshot(null, null, null, null, null, null, null));

        public async IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new BackendFaulted("后端连接已断开");
        }
    }
}

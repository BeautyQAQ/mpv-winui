using System.Threading.Channels;
using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelDispatchTests
{
    [Fact]
    public async Task Ready_snapshot_burst_should_apply_the_latest_values_in_one_ui_callback()
    {
        var backend = new EventPublishingBackend();
        var callbacks = Channel.CreateUnbounded<Action>();
        await using var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator());
        vm.SetUiDispatcher(callback => callbacks.Writer.TryWrite(callback));
        var oldTrack = new TrackInfo(1, "audio", null, "旧音轨", true);
        var latestTrack = new TrackInfo(2, "audio", null, "新音轨", true);
        var initial = PlaybackState.Initial with { CurrentUrl = "current.mp4", IsPlaying = true };

        // 初始化前填好事件流，使就绪批次独立于生产者与事件泵的线程调度。
        backend.Publish(new PlaybackStateChanged(initial with { PositionSeconds = 1 }));
        backend.Publish(new TracksChanged([oldTrack]));
        backend.Publish(new BufferingChanged(true));
        backend.Publish(new PlaybackStateChanged(initial with { PositionSeconds = 9 }));
        backend.Publish(new MediaInfoChanged(new InfoPanelSnapshot("h264", "aac", "SDR", null, null, null, null)));
        backend.Publish(new TracksChanged([latestTrack]));
        backend.Publish(new BufferingChanged(false));
        backend.Publish(new MediaInfoChanged(new InfoPanelSnapshot("hevc", "aac", "HDR", null, null, null, null)));
        await vm.InitializeAsync();
        var callback = await callbacks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.CurrentUrl.Should().BeNull();
        vm.Tracks.Should().BeEmpty();
        callback();

        vm.State.PositionSeconds.Should().Be(9);
        vm.Tracks.Should().Equal(latestTrack);
        vm.IsBuffering.Should().BeFalse();
        vm.InfoPanel.VideoSummary.Should().Contain("hevc");
        vm.InfoPanel.HdrSummary.Should().Be("HDR");
    }
}

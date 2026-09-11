using FluentAssertions;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class InfoPanelViewModelTests
{
    [Fact]
    public void Snapshot_should_format_summary_lines()
    {
        var vm = new InfoPanelViewModel();
        vm.Update(new InfoPanelSnapshot("hevc", "eac3", "HDR10", "3840x2160", "10-bit", "23.976", "forward=8s"));

        vm.VideoSummary.Should().Contain("hevc");
        vm.VideoSummary.Should().Contain("3840x2160");
        vm.HdrSummary.Should().Contain("HDR10");
    }

    [Fact]
    public void Media_updates_should_preserve_output_status_and_notify_hdr_source_changes()
    {
        var vm = new InfoPanelViewModel();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, change) => changes.Add(change.PropertyName);
        vm.SetOutputSummary("HDR10 · 1000 nit");
        vm.Update(new InfoPanelSnapshot("hevc", null, "HDR10 / PQ", null, null, null, null,
            DynamicRange: VideoDynamicRange.Pq));
        vm.IsHdrSource.Should().BeTrue();
        vm.OutputSummary.Should().Be("HDR10 · 1000 nit");
        changes.Should().Contain(nameof(InfoPanelViewModel.IsHdrSource));

        vm.Update(new InfoPanelSnapshot(null, null, null, null, null, null, null));
        vm.IsHdrSource.Should().BeFalse();
        vm.OutputSummary.Should().Be("HDR10 · 1000 nit");
        vm.DecodeSummary.Should().Contain("丢帧 未知");
    }

    [Fact]
    public void Software_fallback_should_be_visible_without_reporting_zero_for_missing_counters()
    {
        var vm = new InfoPanelViewModel();
        vm.Update(new InfoPanelSnapshot(null, null, "SDR", null, null, null, null,
            new VideoDecodeDiagnostics(VideoDecodeMode.Software, "no", null, "yuv420p", null, 0, null), VideoDynamicRange.Sdr));
        vm.DecodeSummary.Should().Contain("软件解码").And.Contain("解码丢帧 0").And.Contain("呈现丢帧 未知");
        vm.IsHdrSource.Should().BeFalse();
    }
}

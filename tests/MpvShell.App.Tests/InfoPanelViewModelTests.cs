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
}

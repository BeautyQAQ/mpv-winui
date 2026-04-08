using FluentAssertions;
using MpvShell.Player.MpvSidecar;

namespace MpvShell.Player.MpvSidecar.Tests;

public sealed class MpvCommandFactoryTests
{
    [Fact]
    public void Seek_command_should_match_json_ipc_shape()
    {
        var json = MpvCommandFactory.SeekRelative(15);

        json.Should().Contain("\"command\"");
        json.Should().Contain("\"seek\"");
        json.Should().Contain("15");
        json.Should().Contain("\"relative\"");
    }
}

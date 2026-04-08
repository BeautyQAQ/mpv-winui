using FluentAssertions;
using MpvShell.Player.MpvSidecar;

namespace MpvShell.Player.MpvSidecar.Tests;

public sealed class MpvProcessManagerTests
{
    [Fact]
    public void Launch_arguments_should_enable_idle_force_window_and_ipc()
    {
        var options = new MpvLaunchOptions("mpv.exe", "mpvshell-test", (nint)1234);
        var args = MpvProcessManager.BuildArguments(options);

        args.Should().Contain("--idle=yes");
        args.Should().Contain("--force-window=yes");
        args.Should().Contain("--input-ipc-server=\\\\.\\pipe\\mpvshell-test");
        args.Should().Contain("--wid=1234");
    }
}

using System.Diagnostics;

namespace MpvShell.Player.MpvSidecar;

public sealed class MpvProcessManager
{
    public static string BuildArguments(MpvLaunchOptions options) =>
        $"--idle=yes --force-window=yes --input-ipc-server=\\\\.\\pipe\\{options.PipeName} --wid={options.HostHandle}";

    public Process Start(MpvLaunchOptions options)
    {
        var psi = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            Arguments = BuildArguments(options),
            UseShellExecute = false,
            RedirectStandardError = true,
        };

        return Process.Start(psi) ?? throw new InvalidOperationException("mpv.exe 启动失败");
    }
}

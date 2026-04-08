namespace MpvShell.Player.MpvSidecar;

public sealed record MpvLaunchOptions(string ExecutablePath, string PipeName, nint HostHandle);

namespace MpvShell.Player.MpvSidecar;

public sealed class LegacyMpvHost
{
    public nint Handle { get; private set; }

    public void Attach(nint handle)
    {
        if (handle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "旧 Sidecar 视频宿主句柄不能为空。");
        }

        Handle = handle;
    }

    public nint GetRequiredHandle()
    {
        if (Handle == 0)
        {
            throw new InvalidOperationException("旧 Sidecar 视频宿主尚未绑定。");
        }

        return Handle;
    }
}

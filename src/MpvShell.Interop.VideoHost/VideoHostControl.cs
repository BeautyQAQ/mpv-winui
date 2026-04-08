using Microsoft.UI.Xaml.Controls;

namespace MpvShell.Interop.VideoHost;

public sealed class VideoHostControl : Grid
{
    public nint ChildWindowHandle { get; private set; }

    public void Attach(nint childHandle)
    {
        ChildWindowHandle = childHandle;
    }
}

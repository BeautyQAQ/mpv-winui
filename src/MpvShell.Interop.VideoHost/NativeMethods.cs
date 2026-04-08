using System.Runtime.InteropServices;

namespace MpvShell.Interop.VideoHost;

internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool MoveWindow(nint hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);
}

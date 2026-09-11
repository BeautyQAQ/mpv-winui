using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MpvShell.Rendering.WinUI.Interop;

/// <summary>读取窗口实际所在显示器，不依赖 Composition SwapChain 的 GetContainingOutput。</summary>
internal static partial class DisplayMonitorNative
{
    // Windows SDK 10.0.26100 winuser.h：MONITOR_DEFAULTTONEAREST。
    private const uint DefaultToNearest = 2;

    internal static unsafe string GetDeviceName(nint windowHandle)
    {
        var monitor = MonitorFromWindow(windowHandle, DefaultToNearest);
        if (monitor == 0)
            throw new InvalidOperationException("无法确定窗口所在的显示器。");

        var info = new MonitorInfoEx { Size = (uint)sizeof(MonitorInfoEx) };
        if (GetMonitorInfo(monitor, ref info) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "读取显示器名称失败。");

        // 固定长度读取避免原生返回缺少终止符时越界。
        return new string((char*)info.DeviceName, 0, 32).TrimEnd('\0');
    }

    [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static partial nint MonitorFromWindow(nint windowHandle, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    private static partial int GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    // MONITORINFOEXW = DWORD cbSize + RECT rcMonitor + RECT rcWork + DWORD dwFlags + WCHAR[32]。
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MonitorInfoEx
    {
        public uint Size;
        public int MonitorLeft;
        public int MonitorTop;
        public int MonitorRight;
        public int MonitorBottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;
        public fixed ushort DeviceName[32];
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace MpvShell.App;

public sealed partial class MainWindow : Window
{
    private bool _closing;
    private bool _canClose;
    public MainWindow()
    {
        InitializeComponent();
        // AppWindow.Resize 使用物理像素。进程已声明 PerMonitorV2，按当前显示器 DPI 换算，
        // 使 150% 缩放的 4K 屏和 100% 的 1080p 屏得到相同的逻辑窗口大小。
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        AppWindow.Resize(new SizeInt32((int)Math.Round(1120 * scale), (int)Math.Round(760 * scale)));
        AppWindow.Closing += OnClosing;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_canClose) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        try { await Player.ShutdownAsync(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"关闭播放器失败：{ex}"); }
        finally
        {
            _canClose = true;
            Close();
        }
    }
}

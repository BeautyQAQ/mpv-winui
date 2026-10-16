using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using MpvShell.App.ViewModels;
using MpvShell.App.Views;
using Windows.Graphics;

namespace MpvShell.App;

public sealed partial class MainWindow : Window
{
    private bool _closing;
    private bool _canClose;
    private Task? _shutdownTask;
    public MainWindow()
    {
        InitializeComponent();
        // AppWindow.Resize 使用物理像素。进程已声明 PerMonitorV2，按当前显示器 DPI 换算，
        // 使 150% 缩放的 4K 屏和 100% 的 1080p 屏得到相同的逻辑窗口大小。
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        AppWindow.Resize(new SizeInt32((int)Math.Round(1120 * scale), (int)Math.Round(760 * scale)));
        AppWindow.Closing += OnClosing;
        // 页面通过 Frame 导航承载：卸载/重新挂载遵循 WinUI 页面生命周期，
        // 页面释放渲染资源但不销毁共享播放会话（见 PlayerPage.ReleaseAsync）。
        RootFrame.Navigate(typeof(PlayerPage));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    /// <summary>
    /// 完整的播放器关闭：先释放当前页面（挂起视频、释放渲染资源），再销毁共享播放会话。
    /// 幂等；窗口关闭与恢复验收探针共用同一入口。
    /// </summary>
    public Task ShutdownPlayerAsync() => _shutdownTask ??= ShutdownPlayerCoreAsync();

    private async Task ShutdownPlayerCoreAsync()
    {
        if (RootFrame.Content is PlayerPage page)
        {
            try { await page.ReleaseAsync(); }
            catch (Exception ex) { Trace.WriteLine($"[app] 关闭页面资源失败：{ex}"); }
        }
        var viewModel = ((App)Application.Current).Services.GetRequiredService<PlayerViewModel>();
        try { await viewModel.DisposeAsync(); }
        catch (Exception ex) { Trace.WriteLine($"[app] 销毁播放器会话失败：{ex}"); }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_canClose) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        try { await ShutdownPlayerAsync(); }
        catch (Exception ex) { Trace.WriteLine($"关闭播放器失败：{ex}"); }
        finally
        {
            _canClose = true;
            Close();
        }
    }

#if DEBUG
    /// <summary>恢复验收探针使用真实导航验证页面卸载/重新挂载。</summary>
    internal Microsoft.UI.Xaml.Controls.Frame DebugRootFrame => RootFrame;
#endif
}

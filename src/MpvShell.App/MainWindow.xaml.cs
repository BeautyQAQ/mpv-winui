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
        AppWindow.Resize(new SizeInt32(1120, 760));
        AppWindow.Closing += OnClosing;
    }

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

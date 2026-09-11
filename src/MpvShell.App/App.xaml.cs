using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MpvShell.App.Diagnostics;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.LibMpv;
using System.Diagnostics;

namespace MpvShell.App;

public partial class App : Application
{
    private Window? _window;
    private readonly SessionTraceListener? _sessionLog;
    public IServiceProvider Services { get; }
    public Window? MainWindowInstance => _window;

    public App()
    {
        _sessionLog = SessionLog.Start();
        UnhandledException += (_, args) => LogUnhandledException("WinUI", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogUnhandledException("AppDomain", args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => LogUnhandledException("TaskScheduler", args.Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _sessionLog?.Close();
        InitializeComponent();

        static void LogUnhandledException(string source, object exception)
        {
            Trace.WriteLine($"[app] {source} 未处理异常：{exception}");
            Trace.Flush();
        }

        var services = new ServiceCollection();
        services.AddSingleton<MpvPlayerSession>();
        services.AddSingleton<IMpvPlayerSession>(provider => provider.GetRequiredService<MpvPlayerSession>());
        services.AddSingleton<IPlayerBackend, LibMpvBackend>();
        services.AddSingleton<PlaybackInteractionCoordinator>();
        services.AddSingleton<GestureDecisionEngine>();
        services.AddSingleton<RecentUrlStore>();
        services.AddSingleton<InfoPanelViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddTransient<MainWindow>();
        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = Services.GetRequiredService<MainWindow>();
        _window.Closed += (_, _) =>
        {
            Trace.WriteLine("[app] 主窗口已关闭，播放资源已释放。");
            _sessionLog?.Close();
        };
        _window.Activate();
    }
}

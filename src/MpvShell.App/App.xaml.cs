using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.LibMpv;
using System.Diagnostics;

namespace MpvShell.App;

public partial class App : Application
{
    private Window? _window;
    public IServiceProvider Services { get; }
    public Window? MainWindowInstance => _window;

    public App()
    {
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MpvShell", "logs");
        try
        {
            Directory.CreateDirectory(logDirectory);
            Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(logDirectory, $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log")));
            Trace.AutoFlush = true;
            Trace.WriteLine($"[{DateTimeOffset.Now:O}] MpvShell 启动，进程 {Environment.ProcessId}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        InitializeComponent();
        UnhandledException += (_, args) => Trace.WriteLine($"未处理异常：{args.Exception}");

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
        _window.Activate();
    }
}

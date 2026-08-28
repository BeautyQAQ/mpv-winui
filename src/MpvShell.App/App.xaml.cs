using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.MpvSidecar;

namespace MpvShell.App;

public partial class App : Application
{
    private Window? _window;
    public IServiceProvider Services { get; }
    public Window? MainWindowInstance => _window;

    public App()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddSingleton<LegacyMpvHost>();
        services.AddSingleton<IPlayerBackend, MpvSidecarBackend>();
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

using Application.Interfaces;
using Application.Services;
using Infrastructure.FileSystem;
using Infrastructure.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DAPLibManager.UI;

// 'using System.Windows' is intentionally omitted to avoid ambiguity between
// the System.Windows.Application class and the Application project namespace.
// WPF base class is resolved via the XAML-generated partial class.
public partial class App
{
    private ServiceProvider _serviceProvider = null!;

    private void App_OnStartup(object sender, System.Windows.StartupEventArgs e)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<AudioFileDetector>();
        services.AddSingleton<IMusicFileScanner, MusicFileScanner>();
        services.AddSingleton<IMetadataReader, TagLibMetadataReader>();

        // Application
        services.AddSingleton<ILibraryScanService, LibraryScanService>();

        // UI
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _serviceProvider.Dispose();
        base.OnExit(e);
    }
}

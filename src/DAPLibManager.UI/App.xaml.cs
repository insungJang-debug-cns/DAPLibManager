using System.IO;
using Application.Interfaces;
using Application.Services;
using Infrastructure.FileSystem;
using Infrastructure.Metadata;
using Infrastructure.Persistence;
using Infrastructure.Playlists;
using Microsoft.EntityFrameworkCore;
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

        // Ensure DB schema and FTS5 index are initialized on startup
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicLibraryDbContext>();
        db.Database.EnsureCreated();
        db.EnsureSchemaUpToDateAsync().GetAwaiter().GetResult();
        db.SetupFts5Async().GetAwaiter().GetResult();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Persistence
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DAPLibManager", "library.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        services.AddDbContextFactory<MusicLibraryDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Infrastructure
        services.AddSingleton<AudioFileDetector>();
        services.AddSingleton<IMusicFileScanner, MusicFileScanner>();
        services.AddSingleton<IMetadataReader, TagLibMetadataReader>();
        services.AddSingleton<ITrackRepository, TrackRepository>();

        // Application
        services.AddSingleton<ILibraryScanService, LibraryScanService>();
        services.AddSingleton<ILibrarySyncService, LibrarySyncService>();
        services.AddSingleton<IPlaylistRepository, M3uPlaylistRepository>();

        // UI
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _serviceProvider.Dispose();
        base.OnExit(e);
    }
}

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Application.Interfaces;
using DAPLibManager.UI.Converters;
using DAPLibManager.UI.Settings;
using Microsoft.Win32;

namespace DAPLibManager.UI;

public partial class MainWindow : Window
{
    private readonly ILibrarySyncService _librarySyncService;
    private readonly ITrackRepository _trackRepository;
    private string? _selectedFolder;
    private readonly DispatcherTimer _dotTimer;
    private int _dotCount;
    private readonly DispatcherTimer _searchDebounce;
    private readonly System.Diagnostics.Stopwatch _scanStopwatch = new();

    public MainWindow(ILibrarySyncService librarySyncService, ITrackRepository trackRepository)
    {
        InitializeComponent();
        _librarySyncService = librarySyncService;
        _trackRepository = trackRepository;

        _dotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _dotTimer.Tick += (_, _) =>
        {
            _dotCount = (_dotCount % 3) + 1;
            var elapsed = (int)_scanStopwatch.Elapsed.TotalSeconds;
            StatusText.Text = $"Scanning{new string('.', _dotCount)}  {elapsed}s";
        };

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ExecuteSearchAsync(SearchBox.Text);
        };

        RestoreLastFolder();
    }

    private void RestoreLastFolder()
    {
        var settings = AppSettings.Load();
        if (settings.LastFolder is not null && Directory.Exists(settings.LastFolder))
        {
            _selectedFolder = settings.LastFolder;
            RelativePathConverter.BasePath = _selectedFolder;
            FolderPathText.Text = _selectedFolder;
            FolderPathText.Foreground = System.Windows.Media.Brushes.Black;
            ScanButton.IsEnabled = true;
        }
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Music Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedFolder = dialog.FolderName;
            RelativePathConverter.BasePath = _selectedFolder;
            FolderPathText.Text = _selectedFolder;
            FolderPathText.Foreground = System.Windows.Media.Brushes.Black;
            ScanButton.IsEnabled = true;
            StatusText.Text = string.Empty;
            TrackListView.ItemsSource = null;

            new AppSettings { LastFolder = _selectedFolder }.Save();
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFolder))
            return;

        SetScanningState(isScanning: true);

        try
        {
            var result = await _librarySyncService.SyncLibraryAsync(_selectedFolder);
            var tracks = await _trackRepository.GetTracksInFolderAsync(_selectedFolder);
            TrackListView.ItemsSource = tracks;
            StatusText.Text = $"{tracks.Count} track(s)  |  +{result.Added}  ~{result.Updated}  -{result.Deleted}  ={result.Unchanged}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Scan] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            SetScanningState(isScanning: false);
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async Task ExecuteSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            if (_selectedFolder is not null)
            {
                var tracks = await _trackRepository.GetTracksInFolderAsync(_selectedFolder);
                TrackListView.ItemsSource = tracks;
                StatusText.Text = $"{tracks.Count} track(s)";
            }
            else
            {
                TrackListView.ItemsSource = null;
                StatusText.Text = string.Empty;
            }
            return;
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = await _trackRepository.SearchTracksAsync(query);
            sw.Stop();

            TrackListView.ItemsSource = results;
            StatusText.Text = $"{results.Count} result(s) for \"{query}\" ({sw.Elapsed.TotalMilliseconds:F1}ms)";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Search] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            StatusText.Text = $"Search error: {ex.Message}";
        }
    }

    private void SetScanningState(bool isScanning)
    {
        ScanButton.IsEnabled = !isScanning;
        SelectFolderButton.IsEnabled = !isScanning;
        ScanProgressBar.Visibility = isScanning ? Visibility.Visible : Visibility.Collapsed;

        if (isScanning)
        {
            _dotCount = 0;
            _scanStopwatch.Restart();
            _dotTimer.Start();
        }
        else
        {
            _dotTimer.Stop();
            _scanStopwatch.Stop();
        }
    }
}

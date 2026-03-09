using System.Windows;
using Application.Interfaces;
using Microsoft.Win32;

namespace DAPLibManager.UI;

public partial class MainWindow : Window
{
    private readonly ILibraryScanService _libraryScanService;
    private string? _selectedFolder;

    public MainWindow(ILibraryScanService libraryScanService)
    {
        InitializeComponent();
        _libraryScanService = libraryScanService;
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
            FolderPathText.Text = _selectedFolder;
            FolderPathText.Foreground = System.Windows.Media.Brushes.Black;
            ScanButton.IsEnabled = true;
            StatusText.Text = string.Empty;
            TrackListView.ItemsSource = null;
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFolder))
            return;

        SetScanningState(isScanning: true);

        try
        {
            var tracks = await _libraryScanService.ScanLibraryAsync(_selectedFolder);
            TrackListView.ItemsSource = tracks;
            StatusText.Text = $"{tracks.Count} track(s) found.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            SetScanningState(isScanning: false);
        }
    }

    private void SetScanningState(bool isScanning)
    {
        ScanButton.IsEnabled = !isScanning;
        SelectFolderButton.IsEnabled = !isScanning;
        StatusText.Text = isScanning ? "Scanning..." : StatusText.Text;
    }
}

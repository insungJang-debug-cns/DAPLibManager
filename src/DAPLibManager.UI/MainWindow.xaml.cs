using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Application.Interfaces;
using DAPLibManager.UI.Converters;
using DAPLibManager.UI.Settings;
using Domain.Entities;
using Microsoft.Win32;

namespace DAPLibManager.UI;

public partial class MainWindow : Window
{
    private readonly ILibrarySyncService _librarySyncService;
    private readonly ITrackRepository _trackRepository;
    private readonly IPlaylistRepository _playlistRepository;
    private string? _selectedFolder;
    private readonly DispatcherTimer _dotTimer;
    private int _dotCount;
    private readonly DispatcherTimer _searchDebounce;
    private readonly System.Diagnostics.Stopwatch _scanStopwatch = new();

    private List<Playlist> _playlists = [];
    private Playlist? _currentPlaylist;
    private ObservableCollection<PlaylistEntry>? _playlistTrackItems;

    private Point _dragStartPoint;
    private Point _playlistDragStartPoint;
    private List<PlaylistEntry>? _dragOriginalOrder;

    public MainWindow(
        ILibrarySyncService librarySyncService,
        ITrackRepository trackRepository,
        IPlaylistRepository playlistRepository)
    {
        InitializeComponent();
        _librarySyncService = librarySyncService;
        _trackRepository = trackRepository;
        _playlistRepository = playlistRepository;

        Loaded += async (_, _) =>
        {
            if (_selectedFolder != null)
                await ExecuteScanAsync();
        };

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
            NewPlaylistButton.IsEnabled = true;
            _ = LoadPlaylistsAsync();
        }
    }

    // ── Folder & Scan ────────────────────────────────────────────────────────

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Music Folder" };
        if (dialog.ShowDialog() != true) return;

        _selectedFolder = dialog.FolderName;
        RelativePathConverter.BasePath = _selectedFolder;
        FolderPathText.Text = _selectedFolder;
        FolderPathText.Foreground = System.Windows.Media.Brushes.Black;
        ScanButton.IsEnabled = true;
        NewPlaylistButton.IsEnabled = true;
        StatusText.Text = string.Empty;
        TrackListView.ItemsSource = null;
        _playlistTrackItems = null;
        PlaylistTrackListView.ItemsSource = null;

        new AppSettings { LastFolder = _selectedFolder }.Save();
        _ = LoadPlaylistsAsync();
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await ExecuteScanAsync();

    private async Task ExecuteScanAsync()
    {
        if (string.IsNullOrEmpty(_selectedFolder)) return;

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

    // ── Search ───────────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
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

    // ── Playlist Panel ───────────────────────────────────────────────────────

    private async Task LoadPlaylistsAsync()
    {
        if (_selectedFolder == null) return;
        _playlists = (await _playlistRepository.GetPlaylistsAsync(_selectedFolder)).ToList();
        PlaylistListView.ItemsSource = null;
        PlaylistListView.ItemsSource = _playlists;
    }

    private void RefreshPlaylistTracks()
    {
        _playlistTrackItems = _currentPlaylist != null
            ? new ObservableCollection<PlaylistEntry>(_currentPlaylist.Entries)
            : null;
        PlaylistTrackListView.ItemsSource = _playlistTrackItems;
    }

    private void PlaylistListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentPlaylist = PlaylistListView.SelectedItem as Playlist;
        DeletePlaylistButton.IsEnabled = _currentPlaylist != null;
        RefreshPlaylistTracks();
    }

    private async void NewPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder == null) return;

        var name = ShowInputDialog("New Playlist", "Playlist name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        var playlist = await _playlistRepository.CreatePlaylistAsync(_selectedFolder, name);
        _playlists.Add(playlist);
        PlaylistListView.ItemsSource = null;
        PlaylistListView.ItemsSource = _playlists;
        PlaylistListView.SelectedItem = playlist;
    }

    private async void DeletePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlaylist == null) return;

        var confirm = MessageBox.Show(
            $"Delete \"{_currentPlaylist.Name}\"?",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        await _playlistRepository.DeletePlaylistAsync(_currentPlaylist);
        _playlists.Remove(_currentPlaylist);
        _currentPlaylist = null;
        PlaylistListView.ItemsSource = null;
        PlaylistListView.ItemsSource = _playlists;
        _playlistTrackItems = null;
        PlaylistTrackListView.ItemsSource = null;
        DeletePlaylistButton.IsEnabled = false;
    }

    // ── Drag & Drop ──────────────────────────────────────────────────────────

    private void TrackListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void TrackListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (TrackListView.SelectedItem is not Track track) return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(TrackListView, new DataObject(typeof(Track), track), DragDropEffects.Copy);
    }

    private void PlaylistListView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Track))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void PlaylistListView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(Track))) return;
        var track = (Track)e.Data.GetData(typeof(Track));

        var target = (e.OriginalSource as FrameworkElement)?.DataContext as Playlist;
        if (target == null) return;

        if (!target.AddTrack(track))
        {
            StatusText.Text = $"\"{track.Title}\" 은(는) 이미 \"{target.Name}\"에 있습니다.";
            return;
        }

        await _playlistRepository.SavePlaylistAsync(target);

        if (_currentPlaylist == target)
            RefreshPlaylistTracks();

        StatusText.Text = $"\"{track.Title}\" → \"{target.Name}\" 추가됨";
    }

    private void PlaylistTrackListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _playlistDragStartPoint = e.GetPosition(null);
    }

    private void PlaylistTrackListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (PlaylistTrackListView.SelectedItem is not PlaylistEntry entry) return;
        if (_playlistTrackItems == null) return;

        var pos = e.GetPosition(null);
        var diff = _playlistDragStartPoint - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragOriginalOrder = [.._playlistTrackItems];

        var result = DragDrop.DoDragDrop(
            PlaylistTrackListView,
            new DataObject(typeof(PlaylistEntry), entry),
            DragDropEffects.Move);

        // 드롭 취소 시 원래 순서로 복원
        if (result == DragDropEffects.None && _dragOriginalOrder != null)
        {
            for (int i = 0; i < _dragOriginalOrder.Count; i++)
            {
                var cur = _playlistTrackItems.IndexOf(_dragOriginalOrder[i]);
                if (cur != i) _playlistTrackItems.Move(cur, i);
            }
        }

        _dragOriginalOrder = null;
    }

    private void PlaylistTrackListView_DragOver(object sender, DragEventArgs e)
    {
        if (_currentPlaylist == null || _playlistTrackItems == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(typeof(Track)))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(typeof(PlaylistEntry)))
        {
            var draggedEntry = (PlaylistEntry)e.Data.GetData(typeof(PlaylistEntry));
            var currentIdx = _playlistTrackItems.IndexOf(draggedEntry);
            var toIndex = GetDropTargetIndex(PlaylistTrackListView, e);

            if (currentIdx >= 0 && currentIdx != toIndex)
                _playlistTrackItems.Move(currentIdx, toIndex);

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void PlaylistTrackListView_Drop(object sender, DragEventArgs e)
    {
        if (_currentPlaylist == null || _playlistTrackItems == null) return;

        // 재정렬 완료 - 현재 ObservableCollection 순서를 Playlist에 저장
        if (e.Data.GetDataPresent(typeof(PlaylistEntry)))
        {
            _currentPlaylist.ReorderEntries(_playlistTrackItems);
            await _playlistRepository.SavePlaylistAsync(_currentPlaylist);
            return;
        }

        // 라이브러리에서 추가
        if (!e.Data.GetDataPresent(typeof(Track))) return;
        var track = (Track)e.Data.GetData(typeof(Track));

        if (!_currentPlaylist.AddTrack(track))
        {
            StatusText.Text = $"\"{track.Title}\" 은(는) 이미 \"{_currentPlaylist.Name}\"에 있습니다.";
            return;
        }

        await _playlistRepository.SavePlaylistAsync(_currentPlaylist);
        _playlistTrackItems.Add(_currentPlaylist.Entries[^1]);
        StatusText.Text = $"\"{track.Title}\" → \"{_currentPlaylist.Name}\" 추가됨";
    }

    private static int GetDropTargetIndex(ListView listView, DragEventArgs e)
    {
        var pos = e.GetPosition(listView);
        for (int i = 0; i < listView.Items.Count; i++)
        {
            if (listView.ItemContainerGenerator.ContainerFromIndex(i) is not ListViewItem item) continue;
            var midY = item.TranslatePoint(new Point(0, item.ActualHeight / 2), listView).Y;
            if (pos.Y < midY) return i;
        }
        return Math.Max(0, listView.Items.Count - 1);
    }

    // ── Library Context Menu ─────────────────────────────────────────────────

    private void TrackContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var cm = (ContextMenu)sender;
        cm.Items.Clear();

        if (TrackListView.SelectedItem == null)
        {
            cm.IsOpen = false;
            return;
        }

        var addMenu = new MenuItem { Header = "재생목록에 추가" };
        if (_playlists.Count == 0)
        {
            addMenu.Items.Add(new MenuItem { Header = "(재생목록 없음)", IsEnabled = false });
        }
        else
        {
            foreach (var pl in _playlists)
            {
                var captured = pl;
                var item = new MenuItem { Header = pl.Name };
                item.Click += async (_, _) => await AddSelectedTrackToPlaylistAsync(captured);
                addMenu.Items.Add(item);
            }
        }
        cm.Items.Add(addMenu);
    }

    private async Task AddSelectedTrackToPlaylistAsync(Playlist playlist)
    {
        if (TrackListView.SelectedItem is not Track track) return;

        if (!playlist.AddTrack(track))
        {
            StatusText.Text = $"\"{track.Title}\" 은(는) 이미 \"{playlist.Name}\"에 있습니다.";
            return;
        }

        await _playlistRepository.SavePlaylistAsync(playlist);

        if (_currentPlaylist == playlist)
        {
            _playlistTrackItems?.Add(playlist.Entries[^1]);
        }

        StatusText.Text = $"\"{track.Title}\" → \"{playlist.Name}\" 추가됨";
    }

    // ── Playlist Track Context Menu ──────────────────────────────────────────

    private void PlaylistTrackContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var cm = (ContextMenu)sender;
        cm.Items.Clear();

        if (_currentPlaylist == null || PlaylistTrackListView.SelectedItem == null)
        {
            cm.IsOpen = false;
            return;
        }

        var removeItem = new MenuItem { Header = "재생목록에서 제거" };
        removeItem.Click += async (_, _) => await RemoveSelectedTrackFromPlaylistAsync();
        cm.Items.Add(removeItem);
    }

    private async Task RemoveSelectedTrackFromPlaylistAsync()
    {
        if (_currentPlaylist == null || _playlistTrackItems == null) return;
        var idx = PlaylistTrackListView.SelectedIndex;
        if (idx < 0) return;

        var entry = _playlistTrackItems[idx];
        _currentPlaylist.RemoveAt(idx);
        await _playlistRepository.SavePlaylistAsync(_currentPlaylist);
        _playlistTrackItems.RemoveAt(idx);
    }

    // ── Scanning State ───────────────────────────────────────────────────────

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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string? ShowInputDialog(string title, string prompt)
    {
        var win = new Window
        {
            Title = title, Width = 300, Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
        };

        var tb = new TextBox { Margin = new Thickness(12, 8, 12, 4), Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        var ok = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
        ok.Click += (_, _) => { win.DialogResult = true; };

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 4, 12, 8)
        };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(12, 10, 12, 0) });
        sp.Children.Add(tb);
        sp.Children.Add(btns);
        win.Content = sp;
        tb.Focus();

        return win.ShowDialog() == true ? tb.Text : null;
    }
}

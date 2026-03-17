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

internal record DirFilterItem(string Display, string? Path);

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
    private List<Track> _allTracks = [];
    private List<Track> _currentTracks = [];

    private Point _dragStartPoint;
    private Point _playlistDragStartPoint;
    private List<PlaylistEntry>? _dragOriginalOrder;
    private PlaylistEntry? _ghostEntry;

    private bool _isPlaying;
    private bool _isSeeking;
    private readonly DispatcherTimer _seekTimer;

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
            StatusText.Text = $"스캔 중{new string('.', _dotCount)}  {elapsed}s";
        };

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ExecuteSearchAsync(SearchBox.Text);
        };

        _seekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _seekTimer.Tick += (_, _) =>
        {
            if (_isSeeking || !MediaPlayer.NaturalDuration.HasTimeSpan) return;
            SeekBar.Value = MediaPlayer.Position.TotalSeconds;
            TimeText.Text = $"{MediaPlayer.Position:mm\\:ss} / {MediaPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
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
            _allTracks = [.. tracks];
            PopulateDirTree();
            ApplyDirFilter();
            StatusText.Text = $"총 {tracks.Count}곡";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Scan] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            StatusText.Text = $"오류: {ex.Message}";
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
            ApplyDirFilter();
            return;
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = await _trackRepository.SearchTracksAsync(query);
            sw.Stop();
            var filtered = ApplyDirFilterToList(results);
            _currentTracks = [.. filtered];
            TrackListView.ItemsSource = _currentTracks;
            StatusText.Text = $"\"{query}\" 검색 결과 {_currentTracks.Count}곡 ({sw.Elapsed.TotalMilliseconds:F1}ms)";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Search] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            StatusText.Text = $"검색 오류: {ex.Message}";
        }
    }

    // ── Directory Filter ─────────────────────────────────────────────────────

    private void PopulateDirTree()
    {
        if (_selectedFolder is null) return;

        DirTreeView.Items.Clear();
        var root = new TreeViewItem
        {
            Header = "전체",
            Tag = (string?)null,
            IsSelected = true,
            IsExpanded = true
        };
        AddDirChildren(root, _selectedFolder);
        DirTreeView.Items.Add(root);
    }

    private static void AddDirChildren(TreeViewItem parent, string dirPath)
    {
        try
        {
            foreach (var sub in Directory.GetDirectories(dirPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var item = new TreeViewItem
                {
                    Header = Path.GetFileName(sub),
                    Tag = sub
                };
                AddDirChildren(item, sub);
                parent.Items.Add(item);
            }
        }
        catch { /* 접근 불가 디렉토리 무시 */ }
    }

    private void ApplyDirFilter()
    {
        var filtered = ApplyDirFilterToList(_allTracks);
        _currentTracks = [.. filtered];
        TrackListView.ItemsSource = _currentTracks;
        StatusText.Text = $"총 {_currentTracks.Count}곡";
    }

    private IEnumerable<Track> ApplyDirFilterToList(IEnumerable<Track> tracks)
    {
        if (DirTreeView.SelectedItem is TreeViewItem { Tag: string selectedPath })
            return tracks.Where(t => t.FilePath.StartsWith(selectedPath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
        return tracks;
    }

    private void DirTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_allTracks.Count == 0) return;
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            _ = ExecuteSearchAsync(SearchBox.Text);
        else
            ApplyDirFilter();
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
        UpdateTrackCount();
    }

    private void UpdateTrackCount()
    {
        var count = _currentPlaylist?.Entries.Count;
        PlaylistTrackCountText.Text = count.HasValue ? $"{count} 곡" : string.Empty;
    }

    private void PlaylistListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentPlaylist = PlaylistListView.SelectedItem as Playlist;
        DeletePlaylistButton.IsEnabled = _currentPlaylist != null;
        PlaylistNameText.Text = _currentPlaylist?.Name ?? string.Empty;
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

    private void PlaylistTrackListView_DragEnter(object sender, DragEventArgs e)
    {
        if (_currentPlaylist == null || _playlistTrackItems == null) return;
        if (!e.Data.GetDataPresent(typeof(Track))) return;
        if (_ghostEntry != null) return; // 이미 ghost 있음

        var track = (Track)e.Data.GetData(typeof(Track));
        if (_currentPlaylist.Entries.Any(en => en.TrackPath.Equals(track.FilePath, StringComparison.OrdinalIgnoreCase)))
            return; // 이미 있는 곡은 ghost 안 만듦

        _ghostEntry = new PlaylistEntry(track.FilePath, $"+ {track.Title}", track.Duration);
        var idx = GetDropTargetIndex(PlaylistTrackListView, e);
        _playlistTrackItems.Insert(idx, _ghostEntry);
    }

    private void PlaylistTrackListView_DragLeave(object sender, DragEventArgs e)
    {
        // 실제로 ListView 밖으로 나갔을 때만 ghost 제거
        var pos = e.GetPosition(PlaylistTrackListView);
        if (pos.X >= 0 && pos.Y >= 0 &&
            pos.X < PlaylistTrackListView.ActualWidth &&
            pos.Y < PlaylistTrackListView.ActualHeight)
            return;

        RemoveGhost();
    }

    private void RemoveGhost()
    {
        if (_ghostEntry != null && _playlistTrackItems != null)
        {
            _playlistTrackItems.Remove(_ghostEntry);
            _ghostEntry = null;
        }
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
            // ghost 이동
            if (_ghostEntry != null)
            {
                var toIndex = GetDropTargetIndex(PlaylistTrackListView, e);
                var curIdx = _playlistTrackItems.IndexOf(_ghostEntry);
                if (curIdx >= 0 && curIdx != toIndex)
                    _playlistTrackItems.Move(curIdx, toIndex);
            }
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

        // 재정렬 완료
        if (e.Data.GetDataPresent(typeof(PlaylistEntry)))
        {
            _currentPlaylist.ReorderEntries(_playlistTrackItems);
            await _playlistRepository.SavePlaylistAsync(_currentPlaylist);
            return;
        }

        // 라이브러리에서 추가 - ghost 위치에 실제 항목 삽입
        if (!e.Data.GetDataPresent(typeof(Track))) return;
        var track = (Track)e.Data.GetData(typeof(Track));

        var insertIdx = _ghostEntry != null ? _playlistTrackItems.IndexOf(_ghostEntry) : _playlistTrackItems.Count;
        RemoveGhost();

        if (!_currentPlaylist.InsertTrack(insertIdx, track))
        {
            StatusText.Text = $"\"{track.Title}\" 은(는) 이미 \"{_currentPlaylist.Name}\"에 있습니다.";
            return;
        }

        _playlistTrackItems.Insert(insertIdx, _currentPlaylist.Entries[insertIdx]);
        await _playlistRepository.SavePlaylistAsync(_currentPlaylist);
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
            Title = title, Width = 320, Height = 150,
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

    // ── Favorites ────────────────────────────────────────────────────────────

    private async void StarButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not Track track) return;
        var newValue = !track.IsFavorite;
        track.SetFavorite(newValue);
        await _trackRepository.SetFavoriteAsync(track.Id, newValue);
        TrackListView.Items.Refresh();
        e.Handled = true; // prevent row selection change
    }

    // ── Playback ─────────────────────────────────────────────────────────────

    private void TrackListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TrackListView.SelectedItem is Track track)
            PlayTrack(track.FilePath, string.IsNullOrEmpty(track.ArtistName)
                ? track.Title : $"{track.Title}  —  {track.ArtistName}");
    }

    private void PlaylistTrackListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistTrackListView.SelectedItem is PlaylistEntry entry)
            PlayTrack(entry.TrackPath, entry.Title);
    }

    private void PlayTrack(string filePath, string title)
    {
        MediaPlayer.Source = new Uri(filePath);
        MediaPlayer.Play();
        _isPlaying = true;
        NowPlayingText.Text = title;
        PlayPauseIcon.Text = "\uE769";
        PlaybackArtImage.Source = AlbumArtLoader.GetForFile(filePath) ?? AlbumArtLoader.DefaultArt;
        PlayPauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        SeekBar.IsEnabled = true;
        _seekTimer.Start();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            MediaPlayer.Pause();
            _isPlaying = false;
            PlayPauseIcon.Text = "\uE768";
            _seekTimer.Stop();
        }
        else
        {
            MediaPlayer.Play();
            _isPlaying = true;
            PlayPauseIcon.Text = "\uE769";
            _seekTimer.Start();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        MediaPlayer.Stop();
        _isPlaying = false;
        _seekTimer.Stop();
        SeekBar.Value = 0;
        TimeText.Text = string.Empty;
        PlayPauseIcon.Text = "\uE768";
        NowPlayingText.Text = string.Empty;
        PlaybackArtImage.Source = AlbumArtLoader.DefaultArt;
    }

    private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (MediaPlayer.NaturalDuration.HasTimeSpan)
            SeekBar.Maximum = MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
    }

    private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        _seekTimer.Stop();
        PlayPauseIcon.Text = "\uE768";
        SeekBar.Value = 0;
        TimeText.Text = string.Empty;
    }

    private void SeekBar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => _isSeeking = true;

    private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        MediaPlayer.Position = TimeSpan.FromSeconds(SeekBar.Value);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => MediaPlayer.Volume = VolumeSlider.Value;
}

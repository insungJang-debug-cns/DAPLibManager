namespace Domain.Entities;

public sealed class PlaylistEntry
{
    public string TrackPath { get; }
    public string Title { get; }
    public TimeSpan Duration { get; }

    public PlaylistEntry(string trackPath, string title, TimeSpan duration)
    {
        TrackPath = trackPath;
        Title = title;
        Duration = duration;
    }
}

public sealed class Playlist
{
    private readonly List<PlaylistEntry> _entries;

    public string Name { get; }
    public string FilePath { get; }
    public IReadOnlyList<PlaylistEntry> Entries => _entries;

    public Playlist(string name, string filePath, IEnumerable<PlaylistEntry>? entries = null)
    {
        Name = name;
        FilePath = filePath;
        _entries = entries?.ToList() ?? [];
    }

    public bool AddTrack(Track track)
    {
        if (_entries.Any(e => e.TrackPath.Equals(track.FilePath, StringComparison.OrdinalIgnoreCase)))
            return false;
        _entries.Add(new PlaylistEntry(track.FilePath, track.Title, track.Duration));
        return true;
    }

    public bool InsertTrack(int index, Track track)
    {
        if (_entries.Any(e => e.TrackPath.Equals(track.FilePath, StringComparison.OrdinalIgnoreCase)))
            return false;
        index = Math.Clamp(index, 0, _entries.Count);
        _entries.Insert(index, new PlaylistEntry(track.FilePath, track.Title, track.Duration));
        return true;
    }

    public void RemoveAt(int index) => _entries.RemoveAt(index);

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _entries.Count) return;
        toIndex = Math.Clamp(toIndex, 0, _entries.Count - 1);
        if (fromIndex == toIndex) return;
        var entry = _entries[fromIndex];
        _entries.RemoveAt(fromIndex);
        _entries.Insert(toIndex, entry);
    }

    public void ReorderEntries(IEnumerable<PlaylistEntry> orderedEntries)
    {
        var newOrder = orderedEntries.ToList();
        _entries.Clear();
        _entries.AddRange(newOrder);
    }
}

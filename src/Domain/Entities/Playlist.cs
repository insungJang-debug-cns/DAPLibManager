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

    public void RemoveAt(int index) => _entries.RemoveAt(index);
}

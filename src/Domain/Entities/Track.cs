namespace Domain.Entities;

public class Track
{
    public Guid Id { get; }
    public string Title { get; private set; }
    public string? ArtistName { get; private set; }
    public string? AlbumTitle { get; private set; }
    public int? TrackNumber { get; private set; }
    public TimeSpan Duration { get; private set; }
    public string? Genre { get; private set; }
    public int? Year { get; private set; }
    public string FilePath { get; private set; }
    public long FileSize { get; private set; }
    public DateTime LastModifiedUtc { get; private set; }

    public Track(
        Guid id,
        string title,
        string filePath,
        string? artistName = null,
        string? albumTitle = null,
        int? trackNumber = null,
        TimeSpan duration = default,
        string? genre = null,
        int? year = null,
        long fileSize = 0,
        DateTime lastModifiedUtc = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Track title cannot be empty.", nameof(title));
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        Id = id;
        Title = title;
        FilePath = filePath;
        ArtistName = artistName;
        AlbumTitle = albumTitle;
        TrackNumber = trackNumber;
        Duration = duration;
        Genre = genre;
        Year = year;
        FileSize = fileSize;
        LastModifiedUtc = lastModifiedUtc;
    }

    public void UpdateFileInfo(long fileSize, DateTime lastModifiedUtc)
    {
        FileSize = fileSize;
        LastModifiedUtc = lastModifiedUtc;
    }

    public void UpdateMetadata(
        string? title = null,
        string? artistName = null,
        string? albumTitle = null,
        int? trackNumber = null,
        TimeSpan? duration = null,
        string? genre = null,
        int? year = null)
    {
        if (title is not null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Track title cannot be empty.", nameof(title));
            Title = title;
        }

        if (artistName is not null) ArtistName = artistName;
        if (albumTitle is not null) AlbumTitle = albumTitle;
        if (trackNumber is not null) TrackNumber = trackNumber;
        if (duration is not null) Duration = duration.Value;
        if (genre is not null) Genre = genre;
        if (year is not null) Year = year;
    }

    public void UpdateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));
        FilePath = filePath;
    }
}

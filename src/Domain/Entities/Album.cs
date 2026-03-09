namespace Domain.Entities;

public class Album
{
    private readonly List<Track> _tracks = [];

    public Guid Id { get; }
    public string Title { get; private set; }
    public Guid ArtistId { get; }
    public int? Year { get; private set; }
    public string? Genre { get; private set; }
    public IReadOnlyCollection<Track> Tracks => _tracks.AsReadOnly();

    public Album(Guid id, string title, Guid artistId, int? year = null, string? genre = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Album title cannot be empty.", nameof(title));
        if (artistId == Guid.Empty)
            throw new ArgumentException("ArtistId must be a valid GUID.", nameof(artistId));

        Id = id;
        Title = title;
        ArtistId = artistId;
        Year = year;
        Genre = genre;
    }

    public void AddTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!_tracks.Contains(track))
            _tracks.Add(track);
    }

    public void UpdateDetails(string? title = null, int? year = null, string? genre = null)
    {
        if (title is not null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Album title cannot be empty.", nameof(title));
            Title = title;
        }

        if (year is not null) Year = year;
        if (genre is not null) Genre = genre;
    }
}

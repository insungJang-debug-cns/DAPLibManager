namespace Domain.Entities;

public class Artist
{
    private readonly List<Album> _albums = [];

    public Guid Id { get; }
    public string Name { get; private set; }
    public IReadOnlyCollection<Album> Albums => _albums.AsReadOnly();

    public Artist(Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Artist name cannot be empty.", nameof(name));

        Id = id;
        Name = name;
    }

    public void AddAlbum(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        if (!_albums.Contains(album))
            _albums.Add(album);
    }

    public void UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Artist name cannot be empty.", nameof(name));
        Name = name;
    }
}

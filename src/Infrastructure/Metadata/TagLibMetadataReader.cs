using Application.Interfaces;
using Domain.Entities;
using TagLib;

namespace Infrastructure.Metadata;

public class TagLibMetadataReader : IMetadataReader
{
    public async Task<Track?> ReadTrackAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            return null;

        return await Task.Run(() => ReadTrack(filePath), cancellationToken);
    }

    private static Track? ReadTrack(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);

            var tag = file.Tag;
            var title = Path.GetFileNameWithoutExtension(filePath);
            var artistName = NullIfEmpty(tag.FirstPerformer);
            var albumTitle = NullIfEmpty(tag.Album);
            var genre = NullIfEmpty(tag.FirstGenre);
            var trackNumber = tag.Track > 0 ? (int?)tag.Track : null;
            var duration = file.Properties?.Duration ?? TimeSpan.Zero;
            var year = tag.Year > 0 ? (int?)tag.Year : null;

            return new Track(
                id: Guid.NewGuid(),
                title: title,
                filePath: filePath,
                artistName: artistName,
                albumTitle: albumTitle,
                trackNumber: trackNumber,
                duration: duration,
                genre: genre,
                year: year
            );
        }
        catch (CorruptFileException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MetadataReader] Corrupt, falling back to filename-only: {filePath}: {ex.Message}");
            return new Track(
                id: Guid.NewGuid(),
                title: Path.GetFileNameWithoutExtension(filePath),
                filePath: filePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MetadataReader] {filePath}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

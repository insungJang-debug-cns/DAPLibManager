using System.Collections.Concurrent;
using Application.Interfaces;
using Domain.Entities;

namespace Application.Services;

public sealed class LibrarySyncService(
    IMusicFileScanner scanner,
    IMetadataReader metadataReader,
    ITrackRepository repository) : ILibrarySyncService
{
    public async Task<SyncResult> SyncLibraryAsync(
        string rootFolder, CancellationToken cancellationToken = default)
    {
        // 1. Enumerate audio files on disk
        var diskPaths = await scanner.ScanAsync(rootFolder, cancellationToken);

        // 2. Build disk snapshot: path → (FileSize, LastModifiedUtc)
        var diskSnap = diskPaths
            .Select(p => new FileInfo(p))
            .Where(fi => fi.Exists)
            .ToDictionary(
                fi => fi.FullName,
                fi => (fi.Length, fi.LastWriteTimeUtc),
                StringComparer.OrdinalIgnoreCase);

        // 3. Load DB snapshot (lightweight — only fingerprint columns)
        var dbSnap = await repository.GetFileSnapshotAsync(rootFolder, cancellationToken);

        // 4. Classify each disk file
        var toInsertPaths = new List<string>();
        var toUpdatePaths = new List<string>();
        var unchanged = 0;

        foreach (var (path, (size, mtime)) in diskSnap)
        {
            if (!dbSnap.TryGetValue(path, out var existing))
            {
                toInsertPaths.Add(path);
            }
            else if (existing.FileSize != size || existing.LastModifiedUtc != mtime)
            {
                toUpdatePaths.Add(path);
            }
            else
            {
                unchanged++;
            }
        }

        // 5. Deleted = in DB but not on disk
        var toDeleteIds = dbSnap
            .Where(kv => !diskSnap.ContainsKey(kv.Key))
            .Select(kv => kv.Value.Id)
            .ToList();

        // 6. Read metadata for new and modified files in parallel
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = cancellationToken
        };

        var newTracks = new ConcurrentBag<Track>();
        await Parallel.ForEachAsync(toInsertPaths, parallelOptions, async (path, ct) =>
        {
            var (size, mtime) = diskSnap[path];
            var track = await metadataReader.ReadTrackAsync(path, ct);
            if (track is not null)
            {
                track.UpdateFileInfo(size, mtime);
                newTracks.Add(track);
            }
        });

        var updatedTracks = new ConcurrentBag<Track>();
        await Parallel.ForEachAsync(toUpdatePaths, parallelOptions, async (path, ct) =>
        {
            var (size, mtime) = diskSnap[path];
            var existingInfo = dbSnap[path];
            var track = await metadataReader.ReadTrackAsync(path, ct);
            if (track is not null)
            {
                // Preserve the existing ID so EF Core updates rather than inserts
                var updated = new Track(
                    id: existingInfo.Id,
                    title: track.Title,
                    filePath: track.FilePath,
                    artistName: track.ArtistName,
                    albumTitle: track.AlbumTitle,
                    trackNumber: track.TrackNumber,
                    duration: track.Duration,
                    genre: track.Genre,
                    year: track.Year,
                    fileSize: size,
                    lastModifiedUtc: mtime);
                updatedTracks.Add(updated);
            }
        });

        // 7. Persist changes
        if (newTracks.Count > 0)
            await repository.BulkInsertTracksAsync(newTracks, cancellationToken);

        foreach (var track in updatedTracks)
            await repository.UpdateTrackAsync(track, cancellationToken);

        if (toDeleteIds.Count > 0)
            await repository.DeleteTracksAsync(toDeleteIds, cancellationToken);

        return new SyncResult(
            Added: newTracks.Count,
            Updated: updatedTracks.Count,
            Deleted: toDeleteIds.Count,
            Unchanged: unchanged);
    }
}

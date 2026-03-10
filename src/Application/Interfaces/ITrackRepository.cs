using Domain.Entities;

namespace Application.Interfaces;

public record TrackFileInfo(Guid Id, long FileSize, DateTime LastModifiedUtc);

public interface ITrackRepository
{
    // Existing
    Task SaveTracksAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Track>> SearchTracksAsync(string query, CancellationToken cancellationToken = default);

    // Incremental sync
    Task<Dictionary<string, TrackFileInfo>> GetFileSnapshotAsync(string rootFolder, CancellationToken cancellationToken = default);
    Task BulkInsertTracksAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default);
    Task UpdateTrackAsync(Track track, CancellationToken cancellationToken = default);
    Task DeleteTracksAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Track>> GetTracksInFolderAsync(string rootFolder, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Track>> GetTracksByFilePathsAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}

using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class TrackRepository(IDbContextFactory<MusicLibraryDbContext> factory) : ITrackRepository
{
    public async Task SaveTracksAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        var trackList = tracks.ToList();
        var filePaths = trackList.Select(t => t.FilePath).ToHashSet();

        // Remove existing tracks with matching file paths (upsert: overwrite on re-scan)
        await context.Tracks
            .Where(t => filePaths.Contains(t.FilePath))
            .ExecuteDeleteAsync(cancellationToken);

        context.Tracks.AddRange(trackList);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Track>> SearchTracksAsync(string query, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Build FTS5 prefix query: "come together" → "come* together*"
        var ftsQuery = BuildFtsQuery(query);

        // FTS5 MATCH via content table join; ORDER BY rank = TF-IDF relevance score
        return await context.Tracks
            .FromSqlInterpolated($"""
                SELECT t.*
                FROM Tracks t
                JOIN TrackSearch s ON t.rowid = s.rowid
                WHERE TrackSearch MATCH {ftsQuery}
                ORDER BY rank
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, TrackFileInfo>> GetFileSnapshotAsync(
        string rootFolder, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Load only the fingerprint columns — no full Track materialization
        return await context.Tracks
            .Where(t => t.FilePath.StartsWith(rootFolder))
            .Select(t => new { t.Id, t.FilePath, t.FileSize, t.LastModifiedUtc })
            .ToDictionaryAsync(
                t => t.FilePath,
                t => new TrackFileInfo(t.Id, t.FileSize, t.LastModifiedUtc),
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);
    }

    public async Task BulkInsertTracksAsync(
        IEnumerable<Track> tracks, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        context.Tracks.AddRange(tracks);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateTrackAsync(Track track, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        context.Tracks.Update(track);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteTracksAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var idSet = ids.ToHashSet();
        await context.Tracks
            .Where(t => idSet.Contains(t.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Track>> GetTracksInFolderAsync(
        string rootFolder, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Tracks
            .Where(t => t.FilePath.StartsWith(rootFolder))
            .OrderBy(t => t.FilePath)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private static string BuildFtsQuery(string query)
    {
        // Strip FTS5 special characters, append * for prefix matching per term
        var terms = query.Trim()
                         .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Select(t => t.Replace("\"", "").Replace("*", "").Replace("'", ""))
                         .Where(t => t.Length > 0);
        return string.Join(" ", terms.Select(t => $"{t}*"));
    }
}

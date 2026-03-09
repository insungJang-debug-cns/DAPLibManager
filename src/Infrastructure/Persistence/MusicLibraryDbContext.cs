using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

// Persistence-only models for schema requirements (future use)
internal sealed class ArtistRecord
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

internal sealed class AlbumRecord
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public Guid ArtistId { get; init; }
    public int? Year { get; init; }
}

public class MusicLibraryDbContext(DbContextOptions<MusicLibraryDbContext> options) : DbContext(options)
{
    public DbSet<Track> Tracks => Set<Track>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MusicLibraryDbContext).Assembly);

        modelBuilder.Entity<ArtistRecord>(b =>
        {
            b.ToTable("Artists");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion<string>();
            b.Property(a => a.Name).IsRequired();
        });

        modelBuilder.Entity<AlbumRecord>(b =>
        {
            b.ToTable("Albums");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion<string>();
            b.Property(a => a.Title).IsRequired();
            b.Property(a => a.ArtistId).HasConversion<string>();
        });
    }

    /// <summary>
    /// Adds FileSize and LastModifiedUtc columns if they don't already exist.
    /// Safe to call on every startup (ignores "duplicate column" errors from SQLite).
    /// </summary>
    public async Task EnsureSchemaUpToDateAsync(CancellationToken cancellationToken = default)
    {
        foreach (var sql in new[]
        {
            "ALTER TABLE Tracks ADD COLUMN FileSize INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Tracks ADD COLUMN LastModifiedUtc TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'"
        })
        {
            try { await Database.ExecuteSqlRawAsync(sql, cancellationToken); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Schema] {sql.Split('\n')[0].Trim()}: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Creates the FTS5 virtual table and sync triggers if they do not already exist,
    /// then rebuilds the index from the current Tracks table contents.
    /// Safe to call on every startup (all statements use IF NOT EXISTS).
    /// </summary>
    public async Task SetupFts5Async(CancellationToken cancellationToken = default)
    {
        // FTS5 content table — stores only the inverted index; text is read from Tracks
        await Database.ExecuteSqlRawAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS TrackSearch USING fts5(
                Title,
                ArtistName,
                AlbumTitle,
                Genre,
                content='Tracks',
                content_rowid='rowid'
            )
            """, cancellationToken);

        await Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER IF NOT EXISTS tracks_ai AFTER INSERT ON Tracks BEGIN
                INSERT INTO TrackSearch(rowid, Title, ArtistName, AlbumTitle, Genre)
                VALUES (new.rowid, new.Title, new.ArtistName, new.AlbumTitle, new.Genre);
            END
            """, cancellationToken);

        await Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER IF NOT EXISTS tracks_ad AFTER DELETE ON Tracks BEGIN
                INSERT INTO TrackSearch(TrackSearch, rowid, Title, ArtistName, AlbumTitle, Genre)
                VALUES ('delete', old.rowid, old.Title, old.ArtistName, old.AlbumTitle, old.Genre);
            END
            """, cancellationToken);

        await Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER IF NOT EXISTS tracks_au AFTER UPDATE ON Tracks BEGIN
                INSERT INTO TrackSearch(TrackSearch, rowid, Title, ArtistName, AlbumTitle, Genre)
                VALUES ('delete', old.rowid, old.Title, old.ArtistName, old.AlbumTitle, old.Genre);
                INSERT INTO TrackSearch(rowid, Title, ArtistName, AlbumTitle, Genre)
                VALUES (new.rowid, new.Title, new.ArtistName, new.AlbumTitle, new.Genre);
            END
            """, cancellationToken);

        // Rebuild index from Tracks on every startup to stay in sync
        // (handles tracks that existed before FTS was first added)
        await Database.ExecuteSqlRawAsync(
            "INSERT INTO TrackSearch(TrackSearch) VALUES('rebuild')",
            cancellationToken);
    }
}

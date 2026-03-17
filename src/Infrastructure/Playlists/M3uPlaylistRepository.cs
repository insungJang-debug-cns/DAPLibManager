using System.IO;
using System.Text;
using Application.Interfaces;
using Domain.Entities;

namespace Infrastructure.Playlists;

public sealed class M3uPlaylistRepository : IPlaylistRepository
{
    private static string PlaylistFolder(string folder) =>
        Path.Combine(folder, "Playlists");

    public async Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(
        string folder, CancellationToken cancellationToken = default)
    {
        var playlistDir = PlaylistFolder(folder);
        if (!Directory.Exists(playlistDir))
            return [];
        var m3uFiles = Directory.GetFiles(playlistDir, "*.m3u", SearchOption.TopDirectoryOnly);
        var playlists = new List<Playlist>();

        foreach (var file in m3uFiles.OrderBy(f => f))
        {
            try
            {
                var playlist = await ParseM3uAsync(file, cancellationToken);
                playlists.Add(playlist);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Playlist] Failed to parse {file}: {ex.Message}");
            }
        }

        return playlists;
    }

    public async Task<Playlist> CreatePlaylistAsync(
        string folder, string name, CancellationToken cancellationToken = default)
    {
        var playlistDir = PlaylistFolder(folder);
        Directory.CreateDirectory(playlistDir);
        var fileName = SanitizeFileName(name) + ".m3u";
        var filePath = Path.Combine(playlistDir, fileName);
        var playlist = new Playlist(name, filePath);
        await SavePlaylistAsync(playlist, cancellationToken);
        return playlist;
    }

    public async Task SavePlaylistAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        var m3uDir = Path.GetDirectoryName(playlist.FilePath)!;
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (var entry in playlist.Entries)
        {
            var seconds = (int)entry.Duration.TotalSeconds;
            sb.AppendLine($"#EXTINF:{seconds},{entry.Title}");
            var relativePath = Path.GetRelativePath(m3uDir, entry.TrackPath).Replace('\\', '/');
            sb.AppendLine(relativePath);
        }

        await File.WriteAllTextAsync(playlist.FilePath, sb.ToString(), Encoding.UTF8, cancellationToken);
    }

    public Task DeletePlaylistAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        if (File.Exists(playlist.FilePath))
            File.Delete(playlist.FilePath);
        return Task.CompletedTask;
    }

    private static async Task<Playlist> ParseM3uAsync(string filePath, CancellationToken cancellationToken)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var m3uDir = Path.GetDirectoryName(filePath)!;
        var entries = new List<PlaylistEntry>();

        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, cancellationToken);

        string? pendingTitle = null;
        var pendingDuration = TimeSpan.Zero;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == "#EXTM3U")
                continue;

            if (trimmed.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                ParseExtInf(trimmed, out pendingDuration, out pendingTitle);
            }
            else if (!trimmed.StartsWith('#'))
            {
                var absolutePath = Path.IsPathRooted(trimmed)
                    ? trimmed
                    : Path.GetFullPath(Path.Combine(m3uDir, trimmed.Replace('/', '\\')));

                var title = pendingTitle ?? Path.GetFileNameWithoutExtension(absolutePath);
                entries.Add(new PlaylistEntry(absolutePath, title, pendingDuration));

                pendingTitle = null;
                pendingDuration = TimeSpan.Zero;
            }
        }

        return new Playlist(name, filePath, entries);
    }

    private static void ParseExtInf(string line, out TimeSpan duration, out string? title)
    {
        duration = TimeSpan.Zero;
        title = null;

        var rest = line["#EXTINF:".Length..];
        var commaIdx = rest.IndexOf(',');

        if (commaIdx >= 0)
        {
            if (int.TryParse(rest.AsSpan(0, commaIdx), out var secs) && secs > 0)
                duration = TimeSpan.FromSeconds(secs);
            title = rest[(commaIdx + 1)..].Trim();
        }
        else if (int.TryParse(rest, out var s) && s > 0)
        {
            duration = TimeSpan.FromSeconds(s);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}

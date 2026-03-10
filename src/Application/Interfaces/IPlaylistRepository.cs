using Domain.Entities;

namespace Application.Interfaces;

public interface IPlaylistRepository
{
    Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(string folder, CancellationToken cancellationToken = default);
    Task<Playlist> CreatePlaylistAsync(string folder, string name, CancellationToken cancellationToken = default);
    Task SavePlaylistAsync(Playlist playlist, CancellationToken cancellationToken = default);
    Task DeletePlaylistAsync(Playlist playlist, CancellationToken cancellationToken = default);
}

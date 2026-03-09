using Domain.Entities;

namespace Application.Interfaces;

public interface ILibraryScanService
{
    Task<IReadOnlyList<Track>> ScanLibraryAsync(string rootFolder, CancellationToken cancellationToken = default);
}

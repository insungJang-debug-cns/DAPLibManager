using Domain.Entities;

namespace Application.Interfaces;

public interface IMetadataReader
{
    Task<Track?> ReadTrackAsync(string filePath, CancellationToken cancellationToken = default);
}

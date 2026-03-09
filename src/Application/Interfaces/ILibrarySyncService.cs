namespace Application.Interfaces;

public record SyncResult(int Added, int Updated, int Deleted, int Unchanged);

public interface ILibrarySyncService
{
    Task<SyncResult> SyncLibraryAsync(string rootFolder, CancellationToken cancellationToken = default);
}

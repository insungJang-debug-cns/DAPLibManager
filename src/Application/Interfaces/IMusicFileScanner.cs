namespace Application.Interfaces;

public interface IMusicFileScanner
{
    /// <summary>
    /// Recursively scans a folder and returns the paths of all supported audio files.
    /// </summary>
    Task<IReadOnlyList<string>> ScanAsync(string folderPath, CancellationToken cancellationToken = default);
}

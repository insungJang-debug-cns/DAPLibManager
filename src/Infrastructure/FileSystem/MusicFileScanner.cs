using Application.Interfaces;

namespace Infrastructure.FileSystem;

public class MusicFileScanner : IMusicFileScanner
{
    private readonly AudioFileDetector _detector;

    public MusicFileScanner(AudioFileDetector detector)
    {
        _detector = detector;
    }

    public Task<IReadOnlyList<string>> ScanAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        var files = ScanFolder(folderPath, cancellationToken);
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    private List<string> ScanFolder(string folderPath, CancellationToken cancellationToken)
    {
        var result = new List<string>();

        foreach (var filePath in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_detector.IsSupported(filePath))
                result.Add(filePath);
        }

        return result;
    }
}

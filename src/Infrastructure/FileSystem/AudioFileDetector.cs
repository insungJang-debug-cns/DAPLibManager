namespace Infrastructure.FileSystem;

public class AudioFileDetector
{
    private static readonly HashSet<string> SupportedExtensions =
        [".mp3", ".flac", ".m4a", ".wav"];

    public bool IsSupported(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension.ToLowerInvariant());
    }

    public IReadOnlyCollection<string> GetSupportedExtensions() => SupportedExtensions;
}

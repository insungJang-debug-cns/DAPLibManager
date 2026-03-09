using System.Collections.Concurrent;
using Application.Interfaces;
using Domain.Entities;

namespace Application.Services;

public class LibraryScanService : ILibraryScanService
{
    private readonly IMusicFileScanner _scanner;
    private readonly IMetadataReader _metadataReader;
    private readonly ITrackRepository _trackRepository;

    public LibraryScanService(
        IMusicFileScanner scanner,
        IMetadataReader metadataReader,
        ITrackRepository trackRepository)
    {
        _scanner = scanner;
        _metadataReader = metadataReader;
        _trackRepository = trackRepository;
    }

    public async Task<IReadOnlyList<Track>> ScanLibraryAsync(
        string rootFolder,
        CancellationToken cancellationToken = default)
    {
        var filePaths = await _scanner.ScanAsync(rootFolder, cancellationToken);

        var tracks = new ConcurrentBag<Track>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(filePaths, options, async (filePath, ct) =>
        {
            var track = await _metadataReader.ReadTrackAsync(filePath, ct);
            if (track is not null)
                tracks.Add(track);
        });

        var result = tracks.ToArray();
        await _trackRepository.SaveTracksAsync(result, cancellationToken);

        return result;
    }
}

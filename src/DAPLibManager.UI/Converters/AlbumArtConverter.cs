using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace DAPLibManager.UI.Converters;

public sealed class AlbumArtConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string filePath) return null;
        return AlbumArtLoader.GetForFile(filePath);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public static class AlbumArtLoader
{
    // 파일 경로 기준 캐시 - 각 트랙의 임베디드 아트를 개별 저장
    private static readonly ConcurrentDictionary<string, BitmapImage?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] CandidateNames =
    [
        "folder.jpg",   "folder.png",
        "cover.jpg",    "cover.png",
        "album.jpg",    "album.png",
        "front.jpg",    "front.png",
        "artwork.jpg",  "artwork.png",
    ];

    public static BitmapImage? GetForFile(string filePath)
        => _cache.GetOrAdd(filePath, LoadFromFile);

    private static BitmapImage? LoadFromFile(string filePath)
    {
        // 1. 폴더 내 이미지 파일 우선 (빠름)
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null)
        {
            foreach (var name in CandidateNames)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                    return LoadImage(path);
            }
        }

        // 2. 파일 자체의 임베디드 아트
        try
        {
            using var file = TagLib.File.Create(filePath);
            var pictures = file.Tag.Pictures;
            if (pictures.Length == 0) return null;

            var data = pictures[0].Data.Data;
            using var ms = new MemoryStream(data);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelHeight = 40;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? LoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelHeight = 40;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

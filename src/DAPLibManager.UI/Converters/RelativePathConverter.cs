using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace DAPLibManager.UI.Converters;

public sealed class RelativePathConverter : IValueConverter
{
    public static string? BasePath { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string fullPath)
            return string.Empty;

        var dir = Path.GetDirectoryName(fullPath) ?? fullPath;

        if (BasePath is not null && dir.StartsWith(BasePath, StringComparison.OrdinalIgnoreCase))
            return dir[BasePath.Length..].TrimStart('\\', '/');

        return dir;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

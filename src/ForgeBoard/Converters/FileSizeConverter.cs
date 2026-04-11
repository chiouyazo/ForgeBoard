using Microsoft.UI.Xaml.Data;

namespace ForgeBoard.Converters;

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        long bytes = value switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            _ => 0,
        };

        return FormatFileSize(bytes);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIndex = 0;
        double size = bytes;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{bytes} B";
        }

        return $"{size:F1} {units[unitIndex]}";
    }
}

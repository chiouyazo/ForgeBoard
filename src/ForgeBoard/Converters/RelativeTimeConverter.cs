using Microsoft.UI.Xaml.Data;

namespace ForgeBoard.Converters;

public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        DateTimeOffset timestamp;
        if (value is DateTimeOffset dto)
        {
            timestamp = dto;
        }
        else if (value is DateTime dt)
        {
            timestamp = dt;
        }
        else
        {
            return string.Empty;
        }

        TimeSpan elapsed = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();

        if (elapsed.TotalSeconds < 60)
        {
            return "just now";
        }
        if (elapsed.TotalMinutes < 60)
        {
            int minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }
        if (elapsed.TotalHours < 24)
        {
            int hours = (int)elapsed.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }
        if (elapsed.TotalDays < 30)
        {
            int days = (int)elapsed.TotalDays;
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }
        if (elapsed.TotalDays < 365)
        {
            int months = (int)(elapsed.TotalDays / 30);
            return months == 1 ? "1 month ago" : $"{months} months ago";
        }

        int years = (int)(elapsed.TotalDays / 365);
        return years == 1 ? "1 year ago" : $"{years} years ago";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

using ForgeBoard.Contracts.Models;
using Microsoft.UI.Xaml.Data;

namespace ForgeBoard.Converters;

public sealed class BuildStatusToFailedVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is BuildStatus status && status == BuildStatus.Failed)
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

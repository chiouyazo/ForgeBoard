using ForgeBoard.Contracts.Models;
using Microsoft.UI.Xaml.Data;

namespace ForgeBoard.Converters;

public sealed class BuildStatusToEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is BuildStatus status)
        {
            return status == BuildStatus.Succeeded
                || status == BuildStatus.Failed
                || status == BuildStatus.Cancelled;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

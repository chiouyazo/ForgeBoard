using Microsoft.UI.Xaml.Data;

namespace ForgeBoard.Converters;

public sealed class StepToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (
            value is int currentStep
            && parameter is string paramStr
            && int.TryParse(paramStr, out int targetStep)
        )
        {
            return currentStep == targetStep ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

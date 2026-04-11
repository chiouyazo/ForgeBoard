using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ForgeBoard.Converters;

public sealed class OriginColorConverter : IValueConverter
{
    private static readonly SolidColorBrush GrayBrush = new SolidColorBrush(
        ColorHelper.FromArgb(255, 158, 158, 158)
    );
    private static readonly SolidColorBrush BlueBrush = new SolidColorBrush(
        ColorHelper.FromArgb(255, 33, 150, 243)
    );
    private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(
        ColorHelper.FromArgb(255, 76, 175, 80)
    );
    private static readonly SolidColorBrush OrangeBrush = new SolidColorBrush(
        ColorHelper.FromArgb(255, 255, 152, 0)
    );

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string origin = (value as string ?? string.Empty);
        return origin switch
        {
            "Local" => GrayBrush,
            "Imported" => BlueBrush,
            "Built" => GreenBrush,
            "BuildChain" => OrangeBrush,
            _ => GrayBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

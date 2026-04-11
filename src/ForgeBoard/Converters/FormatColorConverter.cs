using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ForgeBoard.Converters;

public sealed class FormatColorConverter : IValueConverter
{
    private static readonly SolidColorBrush PurpleBrush = new SolidColorBrush(
        ColorHelper.FromArgb(255, 156, 39, 176)
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
    private static readonly SolidColorBrush GrayBrush = new SolidColorBrush(
        ColorHelper.FromArgb(255, 158, 158, 158)
    );

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string format = (value as string ?? string.Empty).ToUpperInvariant();
        return format switch
        {
            "ISO" => PurpleBrush,
            "VHDX" => BlueBrush,
            "BOX" => GreenBrush,
            "QCOW2" => OrangeBrush,
            _ => GrayBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

using System.Globalization;

namespace Helix.App.Converters;

internal sealed class DisableLabelColorConveter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBusy)
        {
            return isBusy ? Colors.Gray : Colors.White;
        }

        return Colors.White; // Default color when IsBusy is not a bool
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Colors.White;
    }
}

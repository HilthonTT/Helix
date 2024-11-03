using System.Globalization;

namespace Helix.App.Converters;

internal sealed class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPasswordHidden)
        {
            return isPasswordHidden ? 0.5 : 1.0;
        }

        return 1.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double opacity)
        {
            return opacity < 1.0;
        }

        return true;
    }
}

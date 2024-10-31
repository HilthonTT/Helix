using System.Globalization;

namespace Helix.App.Converters;

internal sealed class TranslateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            return LocalizationResourceManager.Instance[key];
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}

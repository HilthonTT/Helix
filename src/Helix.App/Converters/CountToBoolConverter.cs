using System.Globalization;

namespace Helix.App.Converters;

public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasItems = value is int count && count > 0;

        return IsInverted(parameter) ? !hasItems : hasItems;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool IsInverted(object? parameter)
    {
        return parameter is string flag && flag.Equals("invert", StringComparison.OrdinalIgnoreCase);
    }
}

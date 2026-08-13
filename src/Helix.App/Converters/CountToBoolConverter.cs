using System.Globalization;

namespace Helix.App.Converters;

/// <summary>
/// Turns a collection count into a visibility flag: <c>true</c> when the collection
/// holds something. Pass <c>invert</c> as the parameter to drive an empty-state panel
/// from the same binding.
/// </summary>
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

using System.Text.RegularExpressions;

namespace Helix.Application.Core.Validation;

internal static partial class GeneralValidation
{
    internal static bool IsValidIpAddress(string ipAddress)
    {
        return IpAddressRegex().IsMatch(ipAddress);
    }

    [GeneratedRegex(@"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])$")]
    private static partial Regex IpAddressRegex();
}

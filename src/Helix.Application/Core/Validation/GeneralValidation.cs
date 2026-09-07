using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Helix.Application.Core.Validation;

internal static partial class GeneralValidation
{
    private const int MaximumHostLength = 255;

    private const int MaximumLabelLength = 63;

    internal static bool IsDriveLetter(string? letter) =>
        !string.IsNullOrWhiteSpace(letter) &&
        letter.Length == 1 &&
        char.ToUpperInvariant(letter[0]) is >= 'A' and <= 'Z';

    internal static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        string candidate = host.Trim();

        if (candidate.Length > MaximumHostLength)
        {
            return false;
        }

        if (candidate.Length > 2 && candidate[0] == '[' && candidate[^1] == ']')
        {
            candidate = candidate[1..^1];

            return IsIpv6(candidate);
        }

        return IsIpv6(candidate) || IsIpv4(candidate) || IsHostname(candidate);
    }

    private static bool IsIpv4(string candidate) => Ipv4Regex().IsMatch(candidate);

    private static bool IsIpv6(string candidate)
    {
        return candidate.Contains(':', StringComparison.Ordinal) &&
               IPAddress.TryParse(candidate, out IPAddress? address) &&
               address.AddressFamily == AddressFamily.InterNetworkV6;
    }

    private static bool IsHostname(string candidate)
    {
        string[] labels = candidate.Split('.');

        foreach (string label in labels)
        {
            if (label.Length is 0 or > MaximumLabelLength || !HostnameLabelRegex().IsMatch(label))
            {
                return false;
            }
        }

        return labels.Length == 1 || !labels.All(label => label.All(char.IsAsciiDigit));
    }

    [GeneratedRegex(@"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])$")]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"^[A-Za-z0-9_](?:[A-Za-z0-9_-]*[A-Za-z0-9_])?$")]
    private static partial Regex HostnameLabelRegex();
}

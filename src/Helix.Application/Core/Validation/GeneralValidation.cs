using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Helix.Application.Core.Validation;

internal static partial class GeneralValidation
{
    /// <summary>Longest a DNS name may be, per RFC 1035.</summary>
    private const int MaximumHostLength = 255;

    /// <summary>Longest a single dot-separated label may be, per RFC 1035.</summary>
    private const int MaximumLabelLength = 63;

    /// <summary>
    /// Whether the text names a reachable SMB server: an IPv4 address, an IPv6 address
    /// (bare or in URL brackets), or a hostname — <c>nas.local</c>, <c>MYNAS</c>.
    /// </summary>
    /// <remarks>
    /// This replaced a dotted-quad-only check. A NAS is usually reached by name on a
    /// home network, and a name is the only address that survives the server's DHCP
    /// lease moving, so refusing one made the app unusable for that setup.
    /// </remarks>
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

        // The bracketed form is what every other tool prints an IPv6 literal as, so it
        // is accepted and unwrapped rather than rejected on a technicality.
        if (candidate.Length > 2 && candidate[0] == '[' && candidate[^1] == ']')
        {
            candidate = candidate[1..^1];

            return IsIpv6(candidate);
        }

        return IsIpv6(candidate) || IsIpv4(candidate) || IsHostname(candidate);
    }

    private static bool IsIpv4(string candidate) => Ipv4Regex().IsMatch(candidate);

    /// <summary>
    /// Parsed rather than pattern-matched: IPv6 has compressed runs, embedded IPv4 tails
    /// and scope ids, and a regex covering all three is unreadable and usually wrong.
    /// The colon test keeps a bare IPv4 out of this branch, where the framework parser
    /// would accept forms the strict dotted-quad check above deliberately rejects.
    /// </summary>
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

        // A dotted all-numeric name is a malformed IP address, not a hostname: without
        // this, "192.168.1" and "999.999.999.999" would fall through to here and be
        // accepted after the IPv4 check had correctly turned them away. The dot is what
        // makes it a mistake — a single numeric label is a legal computer name, and
        // nobody types "4400" meaning an address.
        return labels.Length == 1 || !labels.All(label => label.All(char.IsAsciiDigit));
    }

    [GeneratedRegex(@"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])$")]
    private static partial Regex Ipv4Regex();

    /// <summary>
    /// One label of a hostname. Underscores are allowed alongside RFC 1123's letters,
    /// digits and hyphens because Windows accepts them in a NetBIOS computer name, and
    /// a share the OS will happily mount should not be refused by the form in front of it.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9_](?:[A-Za-z0-9_-]*[A-Za-z0-9_])?$")]
    private static partial Regex HostnameLabelRegex();
}

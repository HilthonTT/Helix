#if MACCATALYST
using Helix.Application.Abstractions.Connector;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Connector;

[SupportedOSPlatform("maccatalyst")]
internal sealed class MacNetworkLocation(
    IDateTimeProvider dateTimeProvider,
    ILogger<MacNetworkLocation> logger) : INetworkLocation
{
    private const string RoutePath = "/sbin/route";
    private const string ArpPath = "/usr/sbin/arp";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();

    private Task<NetworkLocation?>? _reading;
    private DateTime _expiresAtUtc;

    public bool IsSupported => true;

    public Task<NetworkLocation?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        DateTime now = dateTimeProvider.UtcNow;

        lock (_gate)
        {
            if (_reading is null || (_reading.IsCompleted && now >= _expiresAtUtc))
            {
                _reading = Task.Run(ReadAsync, CancellationToken.None);
                _expiresAtUtc = now + CacheDuration;
            }

            return _reading.WaitAsync(cancellationToken);
        }
    }

    private async Task<NetworkLocation?> ReadAsync()
    {
        try
        {
            string? route = await RunAsync(RoutePath, "-n", "get", "default");
            if (route is null)
            {
                return null;
            }

            string? gatewayText = ValueOf(route, "gateway");
            if (!IPAddress.TryParse(gatewayText, out IPAddress? gateway) ||
                gateway.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            string? arp = await RunAsync(ArpPath, "-n", gateway.ToString());
            string? hardware = arp is null ? null : HardwareAddressIn(arp);

            string id = hardware is null ? $"gateway-ip:{gateway}" : $"gateway:{hardware}";

            return new NetworkLocation(id, ValueOf(route, "interface") ?? gateway.ToString());
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not work out which network this is.");

            return null;
        }
    }

    internal static string? ValueOf(string output, string key)
    {
        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            int colon = trimmed.IndexOf(':');

            if (colon <= 0 || !trimmed[..colon].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = trimmed[(colon + 1)..].Trim();

            return value.Length == 0 ? null : value;
        }

        return null;
    }

    internal static string? HardwareAddressIn(string arpOutput)
    {
        int at = arpOutput.IndexOf(" at ", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        string rest = arpOutput[(at + 4)..].TrimStart();
        int end = rest.IndexOfAny([' ', '\t', '\n', '\r']);
        string candidate = end < 0 ? rest : rest[..end];

        string[] octets = candidate.Split(':');
        if (octets.Length != 6)
        {
            return null;
        }

        var normalized = new string[6];

        for (int i = 0; i < octets.Length; i++)
        {
            if (octets[i].Length is < 1 or > 2 ||
                !byte.TryParse(octets[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
            {
                return null;
            }

            normalized[i] = value.ToString("x2", CultureInfo.InvariantCulture);
        }

        return normalized.All(o => o == "00") ? null : string.Join('-', normalized);
    }

    private async Task<string?> RunAsync(string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };

        if (!process.Start())
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(CommandTimeout);

        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);

            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));

            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            logger.LogDebug("{Command} did not answer within {Timeout}.", fileName, CommandTimeout);

            return null;
        }
    }
}
#endif

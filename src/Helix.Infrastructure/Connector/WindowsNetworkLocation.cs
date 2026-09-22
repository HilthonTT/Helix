using Helix.Application.Abstractions.Connector;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Connector;

[SupportedOSPlatform("windows")]
internal sealed class WindowsNetworkLocation(
    IDateTimeProvider dateTimeProvider,
    ILogger<WindowsNetworkLocation> logger) : INetworkLocation
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    private static readonly IPAddress RoutingProbe = IPAddress.Parse("8.8.8.8");

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
                _reading = Task.Run(Read, CancellationToken.None);
                _expiresAtUtc = now + CacheDuration;
            }

            return _reading.WaitAsync(cancellationToken);
        }
    }

    private NetworkLocation? Read()
    {
        try
        {
            NetworkInterface? adapter = DefaultRouteAdapter();
            if (adapter is null)
            {
                return null;
            }

            IPAddress? gateway = adapter.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));

            if (gateway is null)
            {
                return null;
            }

            string? hardware = HardwareAddressOf(gateway);

            string id = hardware is null ? $"gateway-ip:{gateway}" : $"gateway:{hardware}";

            return new NetworkLocation(id, ProfileName() ?? gateway.ToString());
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not work out which network this is.");

            return null;
        }
    }

    private static NetworkInterface? DefaultRouteAdapter()
    {
        NetworkInterface[] adapters = [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)];

        if (GetBestInterface(ToNative(RoutingProbe), out uint bestIndex) == 0)
        {
            NetworkInterface? best = adapters.FirstOrDefault(n =>
                n.Supports(NetworkInterfaceComponent.IPv4) &&
                n.GetIPProperties().GetIPv4Properties()?.Index == bestIndex);

            if (best is not null && HasIPv4Gateway(best))
            {
                return best;
            }
        }

        return adapters.FirstOrDefault(HasIPv4Gateway);
    }

    private static bool HasIPv4Gateway(NetworkInterface adapter) =>
        adapter.GetIPProperties().GatewayAddresses
            .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

    private static string? HardwareAddressOf(IPAddress address)
    {
        byte[] hardware = new byte[6];
        uint length = (uint)hardware.Length;

        if (SendARP(ToNative(address), 0, hardware, ref length) != 0 || length == 0)
        {
            return null;
        }

        return string.Join('-', hardware.Take((int)length).Select(b => b.ToString("x2")));
    }

    private static string? ProfileName()
    {
#if WINDOWS
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
        {
            return null;
        }

        try
        {
            string? name = Windows.Networking.Connectivity.NetworkInformation
                .GetInternetConnectionProfile()?.ProfileName;

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
#else
        return null;
#endif
    }

    private static uint ToNative(IPAddress address) => BitConverter.ToUInt32(address.GetAddressBytes(), 0);

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

    [DllImport("iphlpapi.dll")]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] pMacAddr, ref uint phyAddrLen);
}

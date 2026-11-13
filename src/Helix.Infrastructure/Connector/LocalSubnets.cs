using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Helix.Infrastructure.Connector;

internal static class LocalSubnets
{
    internal const int WidestPrefix = 24;

    internal const int MaxHosts = 1024;

    internal sealed record Subnet(IPAddress Address, IPAddress Mask);

    internal static List<IPAddress> HostsToSweep(IEnumerable<Subnet> subnets)
    {
        List<Subnet> all = [.. subnets];

        HashSet<uint> own = [.. all.Select(subnet => ToUInt(subnet.Address))];
        HashSet<uint> seen = [];
        List<IPAddress> hosts = [];

        foreach (Subnet subnet in all)
        {
            uint address = ToUInt(subnet.Address);
            int prefix = PrefixOf(subnet.Mask);

            if (prefix is < 1 or > 30 || IsLinkLocal(address) || IsLoopback(address))
            {
                continue;
            }

            prefix = Math.Max(prefix, WidestPrefix);

            uint mask = uint.MaxValue << (32 - prefix);
            uint network = address & mask;
            uint broadcast = network | ~mask;

            for (uint candidate = network + 1; candidate < broadcast; candidate++)
            {
                if (hosts.Count >= MaxHosts)
                {
                    return hosts;
                }

                if (!own.Contains(candidate) && seen.Add(candidate))
                {
                    hosts.Add(FromUInt(candidate));
                }
            }
        }

        return hosts;
    }

    internal static List<Subnet> Current()
    {
        List<Subnet> withGateway = [];
        List<Subnet> without = [];

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback
                        or NetworkInterfaceType.Tunnel
                        or NetworkInterfaceType.Ppp)
                {
                    continue;
                }

                IPInterfaceProperties properties = adapter.GetIPProperties();
                List<Subnet> target = HasGateway(properties) ? withGateway : without;

                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask is not null)
                    {
                        target.Add(new Subnet(unicast.Address, unicast.IPv4Mask));
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return withGateway.Count > 0 ? withGateway : without;
    }

    private static bool HasGateway(IPInterfaceProperties properties)
    {
        try
        {
            return properties.GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                !gateway.Address.Equals(IPAddress.Any));
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or NetworkInformationException)
        {
            return false;
        }
    }

    private static int PrefixOf(IPAddress mask)
    {
        uint bits = ToUInt(mask);
        int prefix = 0;

        while (prefix < 32 && (bits & (0x80000000u >> prefix)) != 0)
        {
            prefix++;
        }

        return prefix;
    }

    private static bool IsLinkLocal(uint address) => (address & 0xFFFF0000u) == 0xA9FE0000u;

    private static bool IsLoopback(uint address) => (address & 0xFF000000u) == 0x7F000000u;

    private static uint ToUInt(IPAddress address) =>
        BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static IPAddress FromUInt(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);

        return new IPAddress(bytes);
    }
}

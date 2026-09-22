using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Helix.Infrastructure.Connector;

internal class WakeOnLan(IDateTimeProvider dateTimeProvider, ILogger<WakeOnLan> logger) : IWakeOnLan
{
    private static readonly int[] Ports = [9, 7];

    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan LookUpTimeout = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();

    private readonly Dictionary<string, DateTime> _sent = new(StringComparer.OrdinalIgnoreCase);

    public bool CanLookUp =>
#if WINDOWS
        true;
#else
        false;
#endif

    public Task<bool> TryWakeAsync(string? macAddress, CancellationToken cancellationToken = default) =>
        WakeAsync(macAddress, respectQuietPeriod: true, cancellationToken);

    public Task<bool> WakeNowAsync(string? macAddress, CancellationToken cancellationToken = default) =>
        WakeAsync(macAddress, respectQuietPeriod: false, cancellationToken);

    private Task<bool> WakeAsync(string? macAddress, bool respectQuietPeriod, CancellationToken cancellationToken)
    {
        byte[]? hardware = MacAddresses.ToBytes(macAddress);
        string? normalized = MacAddresses.Normalize(macAddress);

        if (hardware is null || normalized is null || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        DateTime now = dateTimeProvider.UtcNow;

        lock (_gate)
        {
            if (respectQuietPeriod && _sent.TryGetValue(normalized, out DateTime last) && now - last < Quiet)
            {
                return Task.FromResult(false);
            }

            _sent[normalized] = now;
        }

        return Task.Run(() =>
        {
            bool sent = Send(normalized, hardware);

            if (!sent)
            {
                lock (_gate)
                {
                    if (_sent.TryGetValue(normalized, out DateTime stamped) && stamped == now)
                    {
                        _sent.Remove(normalized);
                    }
                }
            }

            return sent;
        }, CancellationToken.None);
    }

    public Task<string?> LookUpAsync(string host, CancellationToken cancellationToken = default)
    {
        if (!CanLookUp || string.IsNullOrWhiteSpace(host))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.Run(() => Resolve(host), cancellationToken);
    }

    private bool Send(string macAddress, byte[] hardware)
    {
        bool sent = Transmit(BuildPacket(hardware));

        if (sent)
        {
            logger.LogInformation("Sent a wake-up packet to {Mac}.", macAddress);
        }
        else
        {
            logger.LogWarning("No wake-up packet could be sent for {Mac}.", macAddress);
        }

        return sent;
    }

    protected virtual bool Transmit(byte[] packet)
    {
        bool sent = false;

        foreach (IPAddress destination in Destinations())
        {
            foreach (int port in Ports)
            {
                try
                {
                    using var client = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };

                    client.Send(packet, packet.Length, new IPEndPoint(destination, port));

                    sent = true;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Could not send a wake-up packet to {Destination}:{Port}.", destination, port);
                }
            }
        }

        return sent;
    }

    internal static byte[] BuildPacket(byte[] hardware)
    {
        byte[] packet = new byte[6 + (16 * hardware.Length)];

        for (int i = 0; i < 6; i++)
        {
            packet[i] = 0xFF;
        }

        for (int repeat = 0; repeat < 16; repeat++)
        {
            Buffer.BlockCopy(hardware, 0, packet, 6 + (repeat * hardware.Length), hardware.Length);
        }

        return packet;
    }

    private static List<IPAddress> Destinations()
    {
        List<IPAddress> destinations = [IPAddress.Broadcast];

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                        address.IPv4Mask is null)
                    {
                        continue;
                    }

                    IPAddress? broadcast = BroadcastOf(address.Address, address.IPv4Mask);

                    if (broadcast is not null && !destinations.Contains(broadcast))
                    {
                        destinations.Add(broadcast);
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return destinations;
    }

    private static IPAddress? BroadcastOf(IPAddress address, IPAddress mask)
    {
        byte[] host = address.GetAddressBytes();
        byte[] bits = mask.GetAddressBytes();

        if (host.Length != 4 || bits.Length != 4)
        {
            return null;
        }

        byte[] broadcast = new byte[4];

        for (int i = 0; i < 4; i++)
        {
            broadcast[i] = (byte)(host[i] | ~bits[i]);
        }

        return new IPAddress(broadcast);
    }

    private string? Resolve(string host)
    {
#if WINDOWS
        try
        {
            IPAddress? address = AddressOf(host);
            if (address is null)
            {
                return null;
            }

            byte[] hardware = new byte[6];
            uint length = (uint)hardware.Length;

            if (SendARP(BitConverter.ToUInt32(address.GetAddressBytes(), 0), 0, hardware, ref length) != 0 ||
                length != hardware.Length)
            {
                return null;
            }

            return MacAddresses.Normalize(string.Join('-', hardware.Select(b => b.ToString("x2"))));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not look up a hardware address.");

            return null;
        }
#else
        return null;
#endif
    }

    private static IPAddress? AddressOf(string host)
    {
        if (IPAddress.TryParse(host, out IPAddress? parsed))
        {
            return parsed.AddressFamily == AddressFamily.InterNetwork ? parsed : null;
        }

        Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(host);

        if (!lookup.Wait(LookUpTimeout))
        {
            _ = lookup.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return null;
        }

        return lookup.Result.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
    }

    [DllImport("iphlpapi.dll")]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] pMacAddr, ref uint phyAddrLen);
}

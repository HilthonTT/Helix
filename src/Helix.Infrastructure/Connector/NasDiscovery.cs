using Helix.Application.Abstractions.Connector;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Helix.Infrastructure.Connector;

internal class NasDiscovery(ILogger<NasDiscovery> logger) : INasDiscovery
{
    private const int SmbPort = 445;
    private const int MdnsPort = 5353;
    private const int SweepParallelism = 96;

    private static readonly IPAddress MdnsGroup = IPAddress.Parse("224.0.0.251");

    private static readonly TimeSpan MdnsListen = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan MdnsRepeatAfter = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan NameTimeout = TimeSpan.FromMilliseconds(1200);

    private sealed class Found(string address)
    {
        public string Address { get; } = address;
        public string? Name { get; set; }
        public string? HostName { get; set; }
        public string? Model { get; set; }

        public DiscoveredHost ToHost() => new(Address, Name, HostName, Model);
    }

    public async Task<IReadOnlyList<DiscoveredHost>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        List<LocalSubnets.Subnet> subnets = Subnets();
        HashSet<IPAddress> own = [.. subnets.Select(subnet => subnet.Address)];

        Task<List<MdnsMessage.Service>> browsing = Task.Run(() => BrowseAsync(subnets, cancellationToken), cancellationToken);
        Task<List<IPAddress>> sweeping = Task.Run(
            () => SweepAsync(LocalSubnets.HostsToSweep(subnets), cancellationToken),
            cancellationToken);

        List<MdnsMessage.Service> services = await browsing;
        List<IPAddress> answering = await sweeping;

        Dictionary<string, Found> found = new(StringComparer.OrdinalIgnoreCase);

        foreach (MdnsMessage.Service service in services)
        {
            IPAddress? address = service.Address ?? await AddressOfAsync(service.HostName, subnets, cancellationToken);

            if (address is null || own.Contains(address))
            {
                continue;
            }

            Found entry = EntryFor(found, address);
            entry.Name ??= service.Instance;
            entry.HostName ??= service.HostName;
            entry.Model ??= service.Model;
        }

        foreach (IPAddress address in answering)
        {
            EntryFor(found, address);
        }

        await Parallel.ForEachAsync(
            found.Values.Where(entry => entry.Name is null),
            new ParallelOptions { MaxDegreeOfParallelism = SweepParallelism, CancellationToken = cancellationToken },
            async (entry, token) =>
            {
                IPAddress address = IPAddress.Parse(entry.Address);

                entry.Name = await NetBiosNameOfAsync(address, token);

                if (entry.Name is null && entry.HostName is null)
                {
                    entry.HostName = await ReverseNameOfAsync(address, token);
                }
            });

        logger.LogInformation(
            "Network discovery found {Count} file servers ({Announced} announced over mDNS).",
            found.Count,
            services.Count);

        return found.Values.Select(entry => entry.ToHost()).ToList();
    }

    private static Found EntryFor(Dictionary<string, Found> found, IPAddress address)
    {
        string key = address.ToString();

        if (!found.TryGetValue(key, out Found? entry))
        {
            entry = new Found(key);
            found[key] = entry;
        }

        return entry;
    }

    protected virtual List<LocalSubnets.Subnet> Subnets() => LocalSubnets.Current();

    protected virtual async Task<bool> AcceptsSmbAsync(IPAddress address, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);

        using var client = new TcpClient(AddressFamily.InterNetwork);

        try
        {
            await client.ConnectAsync(address, SmbPort, timeout.Token);

            return true;
        }
        catch (Exception ex) when (ex is SocketException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return false;
        }
    }

    protected virtual async Task<List<MdnsMessage.Record>> QueryMdnsAsync(
        IReadOnlyList<LocalSubnets.Subnet> subnets,
        CancellationToken cancellationToken)
    {
        ConcurrentBag<MdnsMessage.Record> records = [];

        await Task.WhenAll(subnets
            .Select(subnet => subnet.Address)
            .Distinct()
            .Select(address => QueryMdnsOnAsync(address, records, cancellationToken)));

        return [.. records];
    }

    protected virtual async Task<string?> NetBiosNameOfAsync(IPAddress address, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(NameTimeout);

        try
        {
            using var client = new UdpClient(AddressFamily.InterNetwork);

            byte[] query = NetBiosStatus.BuildQuery((ushort)Random.Shared.Next(1, ushort.MaxValue));
            await client.SendAsync(query, new IPEndPoint(address, NetBiosStatus.Port), timeout.Token);

            while (true)
            {
                UdpReceiveResult reply = await client.ReceiveAsync(timeout.Token);

                if (reply.RemoteEndPoint.Address.Equals(address))
                {
                    return NetBiosStatus.ParseName(reply.Buffer);
                }
            }
        }
        catch (Exception ex) when (ex is SocketException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }

    protected virtual async Task<string?> ReverseNameOfAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            IPHostEntry entry = await Dns.GetHostEntryAsync(address).WaitAsync(NameTimeout, cancellationToken);

            return string.IsNullOrWhiteSpace(entry.HostName) || IPAddress.TryParse(entry.HostName, out _)
                ? null
                : entry.HostName;
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or ArgumentException)
        {
            return null;
        }
    }

    protected virtual async Task<IPAddress?> AddressOfAsync(
        string? hostName,
        IReadOnlyList<LocalSubnets.Subnet> subnets,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return null;
        }

        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(hostName, AddressFamily.InterNetwork, cancellationToken)
                .WaitAsync(NameTimeout, cancellationToken);

            return LocalSubnets.Preferred(addresses, subnets);
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or ArgumentException)
        {
            return null;
        }
    }

    private async Task<List<MdnsMessage.Service>> BrowseAsync(
        IReadOnlyList<LocalSubnets.Subnet> subnets,
        CancellationToken cancellationToken)
    {
        try
        {
            List<MdnsMessage.Record> records = await QueryMdnsAsync(subnets, cancellationToken);

            return MdnsMessage.ResolveSmbServices(records, subnets);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The mDNS part of network discovery failed; relying on the port sweep.");

            return [];
        }
    }

    private async Task<List<IPAddress>> SweepAsync(List<IPAddress> hosts, CancellationToken cancellationToken)
    {
        ConcurrentBag<IPAddress> answering = [];

        try
        {
            await Parallel.ForEachAsync(
                hosts,
                new ParallelOptions { MaxDegreeOfParallelism = SweepParallelism, CancellationToken = cancellationToken },
                async (address, token) =>
                {
                    if (await AcceptsSmbAsync(address, token))
                    {
                        answering.Add(address);
                    }
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The port sweep part of network discovery failed.");
        }

        return [.. answering];
    }

    private async Task QueryMdnsOnAsync(
        IPAddress local,
        ConcurrentBag<MdnsMessage.Record> records,
        CancellationToken cancellationToken)
    {
        using var listen = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        listen.CancelAfter(MdnsListen);

        try
        {
            using var client = new UdpClient(new IPEndPoint(local, 0));

            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);

            byte[] query = MdnsMessage.BuildQuery(
                (ushort)Random.Shared.Next(1, ushort.MaxValue),
                MdnsMessage.SmbService,
                MdnsMessage.DeviceInfoService);

            var group = new IPEndPoint(MdnsGroup, MdnsPort);

            await client.SendAsync(query, group, listen.Token);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(MdnsRepeatAfter, listen.Token);
                    await client.SendAsync(query, group, listen.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                }
            }, CancellationToken.None);

            while (true)
            {
                UdpReceiveResult reply = await client.ReceiveAsync(listen.Token);

                foreach (MdnsMessage.Record record in MdnsMessage.Parse(reply.Buffer))
                {
                    records.Add(record);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException ex)
        {
            logger.LogDebug(ex, "Could not ask for mDNS announcements on one adapter.");
        }
    }
}

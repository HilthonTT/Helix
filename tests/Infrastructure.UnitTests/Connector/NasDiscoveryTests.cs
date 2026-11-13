using FluentAssertions;
using Helix.Application.Abstractions.Connector;
using Helix.Infrastructure.Connector;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Infrastructure.UnitTests.Connector;

public sealed class NasDiscoveryTests
{
    private sealed class FakeNasDiscovery : NasDiscovery
    {
        public FakeNasDiscovery()
            : base(NullLogger<NasDiscovery>.Instance)
        {
        }

        public HashSet<string> SmbHosts { get; } = [];

        public List<MdnsMessage.Record> Announced { get; } = [];

        public Dictionary<string, string> NetBiosNames { get; } = [];

        public Dictionary<string, string> ReverseNames { get; } = [];

        public Dictionary<string, string> LocalNames { get; } = [];

        public List<string> NetBiosAsked { get; } = [];

        protected override List<LocalSubnets.Subnet> Subnets() =>
            [new(IPAddress.Parse("192.0.2.20"), IPAddress.Parse("255.255.255.0"))];

        protected override Task<bool> AcceptsSmbAsync(IPAddress address, CancellationToken cancellationToken) =>
            Task.FromResult(SmbHosts.Contains(address.ToString()));

        protected override Task<List<MdnsMessage.Record>> QueryMdnsAsync(
            IReadOnlyList<LocalSubnets.Subnet> subnets,
            CancellationToken cancellationToken) =>
            Task.FromResult(Announced);

        protected override Task<string?> NetBiosNameOfAsync(IPAddress address, CancellationToken cancellationToken)
        {
            lock (NetBiosAsked)
            {
                NetBiosAsked.Add(address.ToString());
            }

            return Task.FromResult(NetBiosNames.GetValueOrDefault(address.ToString()));
        }

        protected override Task<string?> ReverseNameOfAsync(IPAddress address, CancellationToken cancellationToken) =>
            Task.FromResult(ReverseNames.GetValueOrDefault(address.ToString()));

        protected override Task<IPAddress?> AddressOfAsync(string? hostName, CancellationToken cancellationToken) =>
            Task.FromResult(hostName is not null && LocalNames.TryGetValue(hostName, out string? address)
                ? IPAddress.Parse(address)
                : null);
    }

    private static void Announce(FakeNasDiscovery discovery, string instance, string hostName, string? address)
    {
        discovery.Announced.Add(new(MdnsMessage.SmbService, MdnsMessage.TypePtr, Target: $"{instance}.{MdnsMessage.SmbService}"));
        discovery.Announced.Add(new($"{instance}.{MdnsMessage.SmbService}", MdnsMessage.TypeSrv, Target: hostName));

        if (address is not null)
        {
            discovery.Announced.Add(new(hostName, MdnsMessage.TypeA, Address: IPAddress.Parse(address)));
        }
    }

    [Fact]
    public async Task DiscoverAsync_Should_MergeAnAnnouncedServerWithItsSweepAnswer()
    {
        var discovery = new FakeNasDiscovery();
        Announce(discovery, "Studio", "ds920.local", "192.0.2.10");
        discovery.SmbHosts.Add("192.0.2.10");

        IReadOnlyList<DiscoveredHost> hosts = await discovery.DiscoverAsync();

        hosts.Should().ContainSingle().Which.Should().Be(new DiscoveredHost("192.0.2.10", "Studio", "ds920.local", null));
        discovery.NetBiosAsked.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_Should_NameSweepAnswersOverNetBiosThenReverseDns()
    {
        var discovery = new FakeNasDiscovery();
        discovery.SmbHosts.UnionWith(["192.0.2.11", "192.0.2.12", "192.0.2.13"]);
        discovery.NetBiosNames["192.0.2.11"] = "QNAP";
        discovery.ReverseNames["192.0.2.12"] = "backup.lan";

        IReadOnlyList<DiscoveredHost> hosts = await discovery.DiscoverAsync();

        hosts.Should().BeEquivalentTo(
        [
            new DiscoveredHost("192.0.2.11", "QNAP", null, null),
            new DiscoveredHost("192.0.2.12", null, "backup.lan", null),
            new DiscoveredHost("192.0.2.13", null, null, null),
        ]);
    }

    [Fact]
    public async Task DiscoverAsync_Should_ResolveAnAnnouncementThatCameWithoutAnAddress()
    {
        var discovery = new FakeNasDiscovery();
        Announce(discovery, "NAS", "nas.local", null);
        discovery.LocalNames["nas.local"] = "192.0.2.30";

        IReadOnlyList<DiscoveredHost> hosts = await discovery.DiscoverAsync();

        hosts.Should().ContainSingle().Which.Address.Should().Be("192.0.2.30");
    }

    [Fact]
    public async Task DiscoverAsync_Should_LeaveOutThisMachine()
    {
        var discovery = new FakeNasDiscovery();
        Announce(discovery, "This Mac", "mac.local", "192.0.2.20");

        IReadOnlyList<DiscoveredHost> hosts = await discovery.DiscoverAsync();

        hosts.Should().BeEmpty();
    }
}

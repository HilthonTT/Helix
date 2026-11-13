using FluentAssertions;
using Helix.Infrastructure.Connector;
using System.Net;

namespace Infrastructure.UnitTests.Connector;

public sealed class LocalSubnetsTests
{
    private static LocalSubnets.Subnet Subnet(string address, string mask) =>
        new(IPAddress.Parse(address), IPAddress.Parse(mask));

    [Fact]
    public void HostsToSweep_Should_ListEveryHostButThisOne()
    {
        List<IPAddress> hosts = LocalSubnets.HostsToSweep([Subnet("192.0.2.20", "255.255.255.0")]);

        hosts.Should().HaveCount(253);
        hosts[0].Should().Be(IPAddress.Parse("192.0.2.1"));
        hosts[^1].Should().Be(IPAddress.Parse("192.0.2.254"));
        hosts.Should().NotContain(IPAddress.Parse("192.0.2.20"));
    }

    [Fact]
    public void HostsToSweep_Should_NarrowAWideSubnetToTheTwoHundredFiftySixAroundThisAddress()
    {
        List<IPAddress> hosts = LocalSubnets.HostsToSweep([Subnet("10.1.7.9", "255.255.0.0")]);

        hosts.Should().HaveCount(253);
        hosts.Should().OnlyContain(host => host.ToString().StartsWith("10.1.7.", StringComparison.Ordinal));
    }

    [Fact]
    public void HostsToSweep_Should_KeepANarrowSubnetAsItIs()
    {
        List<IPAddress> hosts = LocalSubnets.HostsToSweep([Subnet("192.0.2.66", "255.255.255.240")]);

        hosts.Select(host => host.ToString()).Should().Equal(
            Enumerable.Range(65, 14).Where(i => i != 66).Select(i => $"192.0.2.{i}"));
    }

    [Fact]
    public void HostsToSweep_Should_SkipLinkLocalAndPointToPointAddresses()
    {
        LocalSubnets.HostsToSweep(
        [
            Subnet("169.254.10.10", "255.255.0.0"),
            Subnet("198.51.100.1", "255.255.255.254"),
        ]).Should().BeEmpty();
    }

    [Fact]
    public void HostsToSweep_Should_ListASharedSubnetOnceAndSkipEveryOwnAddress()
    {
        List<IPAddress> hosts = LocalSubnets.HostsToSweep(
        [
            Subnet("192.0.2.20", "255.255.255.0"),
            Subnet("192.0.2.21", "255.255.255.0"),
        ]);

        hosts.Should().HaveCount(252).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void HostsToSweep_Should_StopAtTheCap()
    {
        List<LocalSubnets.Subnet> subnets = [.. Enumerable.Range(0, 6).Select(i => Subnet($"10.0.{i}.1", "255.255.255.0"))];

        LocalSubnets.HostsToSweep(subnets).Should().HaveCount(LocalSubnets.MaxHosts);
    }
}

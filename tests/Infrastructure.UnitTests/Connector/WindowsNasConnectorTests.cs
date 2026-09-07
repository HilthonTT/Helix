using FluentAssertions;
using Helix.Infrastructure.Connector;

namespace Infrastructure.UnitTests.Connector;

public sealed class WindowsNasConnectorTests
{
    [Theory]
    [InlineData("192.168.0.10")]
    [InlineData("nas.local")]
    [InlineData("MYNAS")]
    [InlineData("nas_01.example.com")]
    public void ToUncHost_Should_LeaveAddressesAndNamesAlone(string host)
    {
        WindowsNasConnector.ToUncHost(host).Should().Be(host);
    }

    [Fact]
    public void ToUncHost_Should_TrimSurroundingWhitespace()
    {
        WindowsNasConnector.ToUncHost("  nas.local  ").Should().Be("nas.local");
    }

    [Theory]
    [InlineData("fd00::5", "fd00--5.ipv6-literal.net")]
    [InlineData("[fd00::5]", "fd00--5.ipv6-literal.net")]
    [InlineData("2001:db8::1", "2001-db8--1.ipv6-literal.net")]
    [InlineData("::1", "--1.ipv6-literal.net")]
    public void ToUncHost_Should_EncodeIpv6IntoALiteralName(string host, string expected)
    {
        WindowsNasConnector.ToUncHost(host).Should().Be(expected);
    }

    [Fact]
    public void ToUncHost_Should_NormalizeEquivalentSpellingsOfOneAddress()
    {
        string expanded = WindowsNasConnector.ToUncHost("fd00:0:0:0:0:0:0:5");
        string compressed = WindowsNasConnector.ToUncHost("FD00::5");

        expanded.Should().Be(compressed);
    }
}

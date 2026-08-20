using FluentAssertions;
using Helix.Infrastructure.Connector;

namespace Infrastructure.UnitTests.Connector;

/// <summary>
/// Covers the host rendering, which is the only part of the Windows connector that can
/// be exercised without a NAS on the other end.
/// </summary>
/// <remarks>
/// It is also the part most likely to be wrong: a UNC path cannot contain a colon, so an
/// IPv6 address has to be re-spelled into the <c>ipv6-literal.net</c> form before it can
/// be handed to the redirector.
/// </remarks>
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

    /// <summary>
    /// The literal is built from the parsed address, not the typed text, so that the
    /// several legal spellings of one address all reach the same server name.
    /// </summary>
    [Fact]
    public void ToUncHost_Should_NormalizeEquivalentSpellingsOfOneAddress()
    {
        string expanded = WindowsNasConnector.ToUncHost("fd00:0:0:0:0:0:0:5");
        string compressed = WindowsNasConnector.ToUncHost("FD00::5");

        expanded.Should().Be(compressed);
    }
}

using FluentAssertions;
using Helix.Infrastructure.Connector;

namespace Infrastructure.UnitTests.Connector;

public sealed class HostSpellingTests
{
    [Theory]
    [InlineData("192.168.1.6")]
    [InlineData("10.0.0.1")]
    [InlineData("fd00::5")]
    [InlineData("::1")]
    public void IsAddress_Should_RecognizeLiterals(string host)
    {
        HostSpelling.IsAddress(host).Should().BeTrue();
    }

    [Theory]
    [InlineData("NASQNAPTS1677X")]
    [InlineData("nas.local")]
    [InlineData("nas_01.example.com")]
    public void IsAddress_Should_RejectNames(string host)
    {
        HostSpelling.IsAddress(host).Should().BeFalse();
    }

    [Fact]
    public void IsAddress_Should_TreatAnIpv6LiteralNameAsAName()
    {
        HostSpelling.IsAddress("fd00--5.ipv6-literal.net").Should().BeFalse();
    }

    [Fact]
    public void AlternateOf_Should_AnswerNullForAnUnresolvableHost()
    {
        HostSpelling.AlternateOf("helix-test-host.invalid").Should().BeNull();
    }

    [Fact]
    public void AlternateOf_Should_AnswerTheSameThingTwice()
    {
        string? first = HostSpelling.AlternateOf("helix-test-cache.invalid");
        string? second = HostSpelling.AlternateOf("helix-test-cache.invalid");

        second.Should().Be(first);
    }

    [Fact]
    public void AlternateOf_Should_AnswerNull_ForAnAddressWithNoReverseRecord()
    {
        Func<string?> lookup = () => HostSpelling.AlternateOf("192.0.2.1");

        lookup.Should().NotThrow().Which.Should().BeNull();
    }

    [Fact]
    public void AlternateOf_Should_AnswerNull_ForAnIpv6AddressWithNoReverseRecord()
    {
        Func<string?> lookup = () => HostSpelling.AlternateOf("2001:db8::1");

        lookup.Should().NotThrow().Which.Should().BeNull();
    }
}

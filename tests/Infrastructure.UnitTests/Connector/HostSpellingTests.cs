using FluentAssertions;
using Helix.Infrastructure.Connector;

namespace Infrastructure.UnitTests.Connector;

/// <summary>
/// Covers the part of the alternate-spelling lookup that does not depend on what DNS
/// happens to know, which is the classification of a host as an address or a name.
/// </summary>
/// <remarks>
/// The resolution itself is deliberately not tested: it is a real lookup against whatever
/// resolver the machine has, so an assertion about its answer would be an assertion about
/// the network the test is running on. What matters here is that the connector only ever
/// asks for an alternate spelling when there is one to ask for, and that a lookup which
/// answers nothing is a normal outcome rather than a failure.
/// </remarks>
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

    /// <summary>
    /// The <c>ipv6-literal.net</c> form is a name as far as this is concerned, and that is
    /// correct: it is already the spelling a UNC path can carry, so there is nothing for
    /// the hostname switch to convert it into.
    /// </summary>
    [Fact]
    public void IsAddress_Should_TreatAnIpv6LiteralNameAsAName()
    {
        HostSpelling.IsAddress("fd00--5.ipv6-literal.net").Should().BeFalse();
    }

    /// <summary>
    /// A name that cannot resolve is answered with null rather than an exception, and
    /// within the lookup deadline — the connector calls this on a path where the user is
    /// already waiting on a mount.
    /// </summary>
    [Fact]
    public void AlternateOf_Should_AnswerNullForAnUnresolvableHost()
    {
        // .invalid is reserved by RFC 2606 precisely so that it can never resolve.
        HostSpelling.AlternateOf("helix-test-host.invalid").Should().BeNull();
    }

    /// <summary>
    /// Answers are cached, including the ones that found nothing, so thirteen shares of
    /// one NAS cost one lookup rather than thirteen.
    /// </summary>
    [Fact]
    public void AlternateOf_Should_AnswerTheSameThingTwice()
    {
        string? first = HostSpelling.AlternateOf("helix-test-cache.invalid");
        string? second = HostSpelling.AlternateOf("helix-test-cache.invalid");

        second.Should().Be(first);
    }
}

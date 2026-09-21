using FluentAssertions;
using Helix.Domain.Drives;

namespace Application.UnitTests.Features.Drives;

public class MacAddressesTests
{
    [Theory]
    [InlineData("1a-2b-3c-4d-5e-6f")]
    [InlineData("1A:2B:3C:4D:5E:6F")]
    [InlineData("1a2b.3c4d.5e6f")]
    [InlineData("1a2b3c4d5e6f")]
    [InlineData(" 1a 2b 3c 4d 5e 6f ")]
    public void Normalize_Should_AcceptEveryCommonSpelling(string spelling)
    {
        MacAddresses.Normalize(spelling).Should().Be("1a-2b-3c-4d-5e-6f");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1a-2b-3c-4d-5e")]
    [InlineData("1a-2b-3c-4d-5e-6f-70")]
    [InlineData("1g-2b-3c-4d-5e-6f")]
    [InlineData("192.168.1.6")]
    public void Normalize_Should_RefuseAnythingThatIsNotSixOctets(string? spelling)
    {
        MacAddresses.Normalize(spelling).Should().BeNull();
        MacAddresses.IsValid(spelling).Should().BeFalse();
    }

    [Fact]
    public void ToBytes_Should_ReturnTheSixOctets()
    {
        MacAddresses.ToBytes("1a:2b:3c:4d:5e:ff").Should().Equal(0x1a, 0x2b, 0x3c, 0x4d, 0x5e, 0xff);
    }

    [Fact]
    public void RememberMacAddress_Should_ClearIt_WhenGivenSomethingInvalid()
    {
        var drive = Drive.Create(Guid.NewGuid(), "Z", "nas", "Share", "user", "pass");
        drive.RememberMacAddress("1a-2b-3c-4d-5e-6f");

        drive.RememberMacAddress("not a mac");

        drive.MacAddress.Should().BeNull();
    }
}

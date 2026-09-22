using FluentAssertions;
using Helix.Domain.Drives;

namespace Application.UnitTests.Features.Drives;

public class DriveRemoteHostTests
{
    private static Drive NewDrive() =>
        Drive.Create(Guid.NewGuid(), "Z", "192.168.1.6", "Media", "user", "password");

    [Fact]
    public void ReachAwayAt_Should_TrimTheAddress()
    {
        Drive drive = NewDrive();

        drive.ReachAwayAt("  nas.tailnet.ts.net ");

        drive.RemoteHost.Should().Be("nas.tailnet.ts.net");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReachAwayAt_Should_ClearTheAddress_WhenGivenNothing(string? remoteHost)
    {
        Drive drive = NewDrive();
        drive.ReachAwayAt("nas.tailnet.ts.net");

        drive.ReachAwayAt(remoteHost);

        drive.RemoteHost.Should().BeNull();
    }

    [Fact]
    public void ReachAwayAt_Should_IgnoreTheHomeAddressItself()
    {
        Drive drive = NewDrive();

        drive.ReachAwayAt("192.168.1.6");

        drive.RemoteHost.Should().BeNull();
    }
}

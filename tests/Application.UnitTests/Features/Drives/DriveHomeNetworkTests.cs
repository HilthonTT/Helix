using FluentAssertions;
using Helix.Domain.Drives;

namespace Application.UnitTests.Features.Drives;

public sealed class DriveHomeNetworkTests
{
    private static Drive PinnedTo(string networkId)
    {
        var drive = Drive.Create(Guid.NewGuid(), "Z", "nas.local", "Media", "user", "password");
        drive.PinToNetwork(networkId, "Home");

        return drive;
    }

    [Fact]
    public void IsAwayFrom_Should_BeTrue_OnAnotherIdentifiedNetwork()
    {
        PinnedTo("gateway:aa-bb-cc-dd-ee-ff").IsAwayFrom("gateway:11-22-33-44-55-66").Should().BeTrue();
    }

    [Fact]
    public void IsAwayFrom_Should_BeFalse_OnTheHomeNetwork()
    {
        PinnedTo("gateway:aa-bb-cc-dd-ee-ff").IsAwayFrom("GATEWAY:AA-BB-CC-DD-EE-FF").Should().BeFalse();
    }

    [Fact]
    public void IsAwayFrom_Should_BeFalse_WhenTheGatewayHardwareCouldNotBeRead()
    {
        PinnedTo("gateway:aa-bb-cc-dd-ee-ff").IsAwayFrom("gateway-ip:192.168.1.1").Should().BeFalse();
    }

    [Fact]
    public void IsAwayFrom_Should_CompareAddresses_WhenTheDriveWasPinnedByAddress()
    {
        Drive drive = PinnedTo("gateway-ip:192.168.1.1");

        drive.IsAwayFrom("gateway-ip:192.168.1.1").Should().BeFalse();
        drive.IsAwayFrom("gateway-ip:10.0.0.1").Should().BeTrue();
    }

    [Fact]
    public void IsAwayFrom_Should_BeFalse_WhenTheNetworkIsUnknown()
    {
        PinnedTo("gateway:aa-bb-cc-dd-ee-ff").IsAwayFrom(null).Should().BeFalse();
    }
}

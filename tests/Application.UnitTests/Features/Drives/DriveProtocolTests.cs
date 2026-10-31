using FluentAssertions;
using Helix.Domain.Drives;

namespace Application.UnitTests.Features.Drives;

public sealed class DriveProtocolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_Should_DefaultToSmb()
    {
        var drive = Drive.Create(UserId, "Z", "nas.local", "Media", "user", "password");

        drive.Protocol.Should().Be(DriveProtocol.Smb);
        drive.Port.Should().BeNull();
    }

    [Fact]
    public void Create_Should_AcceptAnNfsDriveWithoutCredentials()
    {
        var drive = Drive.Create(UserId, "Z", "nas.local", "/volume1/media", "", "", protocol: DriveProtocol.Nfs);

        drive.Protocol.Should().Be(DriveProtocol.Nfs);
        drive.Username.Should().BeEmpty();
        drive.Password.Should().BeEmpty();
    }

    [Fact]
    public void Create_Should_StillRequireCredentials_ForWebDav()
    {
        Action create = () => Drive.Create(UserId, "Z", "nas.local", "home", "", "", protocol: DriveProtocol.WebDav);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Should_KeepAPort_OnlyForWebDav()
    {
        Drive.Create(UserId, "Z", "nas.local", "home", "u", "p", protocol: DriveProtocol.WebDav, port: 5006)
            .Port.Should().Be(5006);

        Drive.Create(UserId, "Z", "nas.local", "share", "u", "p", port: 5006)
            .Port.Should().BeNull();

        Drive.Create(UserId, "Z", "nas.local", "/export", "", "", protocol: DriveProtocol.Nfs, port: 5006)
            .Port.Should().BeNull();
    }

    [Fact]
    public void Create_Should_DropTheDefaultHttpsPort()
    {
        Drive.Create(UserId, "Z", "nas.local", "home", "u", "p", protocol: DriveProtocol.WebDav, port: 443)
            .Port.Should().BeNull();
    }

    [Fact]
    public void Update_Should_ForgetThePort_WhenTheDriveLeavesWebDav()
    {
        var drive = Drive.Create(UserId, "Z", "nas.local", "home", "u", "p", protocol: DriveProtocol.WebDav, port: 5006);

        drive.Update("Z", "nas.local", "home", "u", "p", true, false, false, DriveProtocol.Smb, 5006);

        drive.Protocol.Should().Be(DriveProtocol.Smb);
        drive.Port.Should().BeNull();
    }

    [Theory]
    [InlineData("/volume1/media/", "volume1/media")]
    [InlineData(@"\volume1\media", "volume1/media")]
    [InlineData(" remote.php//dav/files/me ", "remote.php/dav/files/me")]
    [InlineData("/", "")]
    public void NormalizePath_Should_TrimSlashesAndUnifySeparators(string path, string expected)
    {
        DriveProtocols.NormalizePath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(DriveProtocol.Smb, null, new[] { 445, 139 })]
    [InlineData(DriveProtocol.Nfs, null, new[] { 2049 })]
    [InlineData(DriveProtocol.WebDav, null, new[] { 443 })]
    [InlineData(DriveProtocol.WebDav, 5006, new[] { 5006 })]
    public void ProbePortsFor_Should_KnockOnTheProtocolsOwnPort(DriveProtocol protocol, int? port, int[] expected)
    {
        DriveProtocols.ProbePortsFor(protocol, port).Should().Equal(expected);
    }
}

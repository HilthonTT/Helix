using FluentAssertions;
using Helix.Domain.Drives;
using Helix.Infrastructure.Connector;

namespace Infrastructure.UnitTests.Connector;

public sealed class ProtocolRemoteNameTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Drive Smb(string share) =>
        Drive.Create(UserId, "Z", "nas.local", share, "user", "password");

    private static Drive WebDav(string path, int? port = null) =>
        Drive.Create(UserId, "Z", "nas.local", path, "user", "password", protocol: DriveProtocol.WebDav, port: port);

    private static Drive Nfs(string path) =>
        Drive.Create(UserId, "Z", "nas.local", path, "", "", protocol: DriveProtocol.Nfs);

    [Fact]
    public void RemoteFor_Should_LeaveAnSmbShareAsItWas()
    {
        WindowsNasConnector.RemoteFor(Smb("Media"), "nas.local").Should().Be(@"\\nas.local\Media");
    }

    [Fact]
    public void RemoteFor_Should_AddressWebDavOverHttps()
    {
        WindowsNasConnector.RemoteFor(WebDav("home"), "nas.local").Should().Be(@"\\nas.local@SSL\home");
    }

    [Fact]
    public void RemoteFor_Should_CarryAWebDavPort()
    {
        WindowsNasConnector.RemoteFor(WebDav("/remote.php/dav/files/me/", 5006), "nas.local")
            .Should().Be(@"\\nas.local@SSL@5006\remote.php\dav\files\me");
    }

    [Fact]
    public void RemoteFor_Should_UseTheDavRoot_ForAnEmptyPath()
    {
        WindowsNasConnector.RemoteFor(WebDav("/"), "nas.local").Should().Be(@"\\nas.local@SSL\DavWWWRoot");
    }

    [Fact]
    public void RemoteFor_Should_TurnAnNfsExportIntoAUncPath()
    {
        WindowsNasConnector.RemoteFor(Nfs("/volume1/media"), "nas.local").Should().Be(@"\\nas.local\volume1\media");
    }

    [Fact]
    public void SharePathFor_Should_MatchWhatTheRemoteNameCarriesAfterTheServer()
    {
        WindowsNasConnector.SharePathFor(Nfs("/volume1/media/")).Should().Be(@"volume1\media");
        WindowsNasConnector.SharePathFor(WebDav("home", 5006)).Should().Be("home");
    }
}

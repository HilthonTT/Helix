using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public sealed class DisconnectDriveTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly DisconnectDrive _disconnectDrive;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IDriveMonitor _driveMonitorMock;

    private readonly Drive _drive;

    public DisconnectDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _driveMonitorMock = Substitute.For<IDriveMonitor>();

        _driveMonitorMock.Suppress(Arg.Any<IEnumerable<string>>()).Returns(Substitute.For<IDisposable>());

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _drive = Drive.Create(UserId, "E", "192.168.0.1", "Media", "user", "password");

        _driveRepositoryMock.GetByIdAsNoTrackingAsync(_drive.Id, Arg.Any<CancellationToken>()).Returns(_drive);

        _disconnectDrive = new(_driveRepositoryMock, _loggedInUserMock, _nasConnectorMock, _driveMonitorMock);
    }

    [Fact]
    public async Task Handle_Should_Unmount_WhenTheLetterIsMountedFromTheDrivesShare()
    {
        _nasConnectorMock.IsMountedFrom(_drive).Returns(true);
        _nasConnectorMock.DisconnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        Result result = await _disconnectDrive.Handle(new DisconnectDrive.Request(_drive.Id));

        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.Received(1).DisconnectAsync(_drive, Arg.Any<CancellationToken>());
        _driveMonitorMock.Received(1).Suppress(Arg.Is<IEnumerable<string>>(letters => letters.Contains("E")));
    }

    [Fact]
    public async Task Handle_Should_NotTouchTheLetter_WhenSomethingElseHoldsIt()
    {
        _nasConnectorMock.IsMountedFrom(_drive).Returns(false);
        _nasConnectorMock.IsConnected("E").Returns(true);

        Result result = await _disconnectDrive.Handle(new DisconnectDrive.Request(_drive.Id));

        result.Error.Should().Be(DriveErrors.LetterInUse("E"));
        await _nasConnectorMock.DidNotReceive().DisconnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_DoNothing_WhenTheDriveIsAlreadyDown()
    {
        _nasConnectorMock.IsMountedFrom(_drive).Returns(false);
        _nasConnectorMock.IsConnected("E").Returns(false);

        Result result = await _disconnectDrive.Handle(new DisconnectDrive.Request(_drive.Id));

        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.DidNotReceive().DisconnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }
}

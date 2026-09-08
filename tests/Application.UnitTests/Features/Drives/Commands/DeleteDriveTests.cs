using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public sealed class DeleteDriveTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly DeleteDrive _deleteDrive;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly IDriveGroupRepository _driveGroupRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IDriveMonitor _driveMonitorMock;

    public DeleteDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _driveGroupRepositoryMock = Substitute.For<IDriveGroupRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _driveMonitorMock = Substitute.For<IDriveMonitor>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveGroupRepositoryMock.GetAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);
        _driveMonitorMock.Suppress(Arg.Any<IEnumerable<string>>()).Returns(Substitute.For<IDisposable>());
        _nasConnectorMock.DisconnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        _deleteDrive = new(
            _driveRepositoryMock,
            _driveGroupRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _driveMonitorMock);
    }

    private Drive Have(bool persistent = false)
    {
        Drive drive = Drive.Create(UserId, "Z", "nas.local", "Media", "user", "password", persistent: persistent);

        _driveRepositoryMock.GetByIdAsync(drive.Id, Arg.Any<CancellationToken>()).Returns(drive);

        return drive;
    }

    [Fact]
    public async Task Handle_Should_UnmountATemporaryDrive_WhenItIsMounted()
    {
        Drive drive = Have();
        _nasConnectorMock.IsMountedFrom(drive).Returns(true);

        Result result = await _deleteDrive.Handle(new DeleteDrive.Request(drive.Id));

        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.Received(1).DisconnectAsync(drive, Arg.Any<CancellationToken>());
        _driveRepositoryMock.Received(1).Remove(drive);
    }

    [Fact]
    public async Task Handle_Should_KeepTheDrive_WhenItsMountCannotBeTakenDown()
    {
        Drive drive = Have();
        Error unmountError = DriveErrors.FailedToDisconnect("The device is in use.");

        _nasConnectorMock.IsMountedFrom(drive).Returns(true);
        _nasConnectorMock.DisconnectAsync(drive, Arg.Any<CancellationToken>()).Returns(Result.Failure(unmountError));

        Result result = await _deleteDrive.Handle(new DeleteDrive.Request(drive.Id));

        result.Error.Should().Be(unmountError);
        _driveRepositoryMock.DidNotReceive().Remove(Arg.Any<Drive>());
        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_NotTouchTheConnector_WhenATemporaryDriveIsDown()
    {
        Drive drive = Have();
        _nasConnectorMock.IsMountedFrom(drive).Returns(false);

        Result result = await _deleteDrive.Handle(new DeleteDrive.Request(drive.Id));

        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.DidNotReceive().DisconnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_CancelAPersistentMapping_EvenWhenItIsDown_AndDeleteRegardless()
    {
        Drive drive = Have(persistent: true);

        _nasConnectorMock.IsMountedFrom(drive).Returns(false);
        _nasConnectorMock.DisconnectAsync(drive, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToDisconnect("Not connected.")));

        Result result = await _deleteDrive.Handle(new DeleteDrive.Request(drive.Id));

        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.Received(1).DisconnectAsync(drive, Arg.Any<CancellationToken>());
        _driveRepositoryMock.Received(1).Remove(drive);
    }

    [Fact]
    public async Task Handle_Should_PruneTheDriveFromEveryGroup()
    {
        Drive drive = Have();
        DriveGroup group = DriveGroup.Create(UserId, "Home", [drive.Id, Guid.NewGuid()]);

        _driveGroupRepositoryMock.GetAsync(UserId, Arg.Any<CancellationToken>()).Returns([group]);

        await _deleteDrive.Handle(new DeleteDrive.Request(drive.Id));

        group.DriveIds.Should().NotContain(drive.Id);
    }
}

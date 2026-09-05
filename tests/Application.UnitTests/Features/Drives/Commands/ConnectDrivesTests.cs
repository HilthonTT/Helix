using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

/// <summary>
/// Covers acting on an ad-hoc selection of rows: which of the named drives are actually
/// touched, and what happens to an id that names nothing.
/// </summary>
public class ConnectDrivesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly ConnectDrives _connectDrives;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IDriveMonitor _driveMonitorMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly IDateTimeProvider _dateTimeProviderMock;

    public ConnectDrivesTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _driveMonitorMock = Substitute.For<IDriveMonitor>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        // Nothing mounted, so every named drive is a candidate to be connected.
        _nasConnectorMock.GetConnectedLetters().Returns([]);
        _nasConnectorMock.ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _nasConnectorMock.DisconnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _driveMonitorMock.Suppress(Arg.Any<IEnumerable<string>>())
            .Returns(Substitute.For<IDisposable>());

        _connectDrives = new(
            _driveRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _driveMonitorMock,
            _dateTimeProviderMock);
    }

    private static Drive Media { get; } =
        Drive.Create(UserId, "Z", "nas.local", "Media", "user", "password");

    private static Drive Backup { get; } =
        Drive.Create(UserId, "Y", "nas.local", "Backup", "user", "password");

    private static Drive Archive { get; } =
        Drive.Create(UserId, "X", "nas.local", "Archive", "user", "password");

    private void HaveDrives(params Drive[] drives) =>
        _driveRepositoryMock.GetAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([.. drives]);

    [Fact]
    public async Task Handle_Should_ConnectOnlyTheDrivesNamed()
    {
        // Arrange
        HaveDrives(Media, Backup, Archive);

        // Act
        Result result = await _connectDrives.Handle(new ConnectDrives.Request([Media.Id, Archive.Id]));

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).ConnectAsync(Media, Arg.Any<CancellationToken>());
        await _nasConnectorMock.Received(1).ConnectAsync(Archive, Arg.Any<CancellationToken>());
        await _nasConnectorMock.DidNotReceive().ConnectAsync(Backup, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_DisconnectOnlyTheDrivesNamed_WhenAskedTo()
    {
        // Arrange
        HaveDrives(Media, Backup);

        // Only the two of them that are up can be taken down.
        _nasConnectorMock.GetConnectedLetters().Returns(["Z", "Y"]);

        // Act
        Result result = await _connectDrives.Handle(
            new ConnectDrives.Request([Media.Id], Disconnect: true));

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).DisconnectAsync(Media, Arg.Any<CancellationToken>());
        await _nasConnectorMock.DidNotReceive().DisconnectAsync(Backup, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_IgnoreIdsThatNameNothing()
    {
        // A row deleted from another window while the selection was held.
        HaveDrives(Media);

        // Act
        Result result = await _connectDrives.Handle(
            new ConnectDrives.Request([Media.Id, Guid.NewGuid()]));

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).ConnectAsync(Media, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ConnectADriveOnce_WhenItsIdIsRepeated()
    {
        // Arrange
        HaveDrives(Media);

        // Act
        Result result = await _connectDrives.Handle(
            new ConnectDrives.Request([Media.Id, Media.Id]));

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).ConnectAsync(Media, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_SuppressTheLettersItTouches()
    {
        // Without this the monitor reads a deliberate unmount as the NAS dropping, and the
        // watchdog puts every drive straight back.
        HaveDrives(Media, Backup);

        _nasConnectorMock.GetConnectedLetters().Returns(["Z", "Y"]);

        // Act
        await _connectDrives.Handle(new ConnectDrives.Request([Media.Id], Disconnect: true));

        // Assert
        _driveMonitorMock.Received(1).Suppress(Arg.Is<IEnumerable<string>>(letters =>
            letters.SequenceEqual(new[] { "Z" })));
    }

    [Fact]
    public async Task Handle_Should_DoNothing_WhenNothingWasSelected()
    {
        // Act
        Result result = await _connectDrives.Handle(new ConnectDrives.Request([]));

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _driveRepositoryMock.DidNotReceive().GetAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReportEveryFailure()
    {
        // Arrange
        HaveDrives(Media, Backup);

        _nasConnectorMock.ConnectAsync(Backup, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("The share refused it.")));

        // Act
        Result result = await _connectDrives.Handle(
            new ConnectDrives.Request([Media.Id, Backup.Id]));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("Y").And.Contain("The share refused it.");
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenNobodyIsSignedIn()
    {
        // Arrange
        _loggedInUserMock.IsLoggedIn.Returns(false);

        // Act
        Result result = await _connectDrives.Handle(new ConnectDrives.Request([Media.Id]));

        // Assert
        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }
}

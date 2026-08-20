using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

/// <summary>
/// Covers the split between the two callers: the button the user presses, which means
/// every drive, and the unattended passes, which mean only the drives opted into them.
/// </summary>
public class ConnectAllDrivesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly ConnectAllDrives _connectAllDrives;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;

    public ConnectAllDrivesTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        // Nothing is mounted, so every drive is a candidate before the flag is applied.
        _nasConnectorMock.GetConnectedLetters().Returns([]);
        _nasConnectorMock.ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _connectAllDrives = new(_driveRepositoryMock, _loggedInUserMock, _nasConnectorMock);
    }

    private static Drive Automatic { get; } =
        Drive.Create(UserId, "Z", "nas.local", "Media", "user", "password", autoConnect: true);

    private static Drive Manual { get; } =
        Drive.Create(UserId, "Y", "nas.local", "Backup", "user", "password", autoConnect: false);

    private void HaveDrives(params Drive[] drives) =>
        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([.. drives]);

    [Fact]
    public async Task Handle_Should_ConnectEveryDrive_WhenTheUserAskedForIt()
    {
        // Arrange
        HaveDrives(Automatic, Manual);

        // Act
        Result result = await _connectAllDrives.Handle();

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).ConnectAsync(Automatic, Arg.Any<CancellationToken>());
        await _nasConnectorMock.Received(1).ConnectAsync(Manual, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_SkipDrivesHeldBack_WhenThePassIsUnattended()
    {
        // Arrange
        HaveDrives(Automatic, Manual);

        // Act
        Result result = await _connectAllDrives.Handle(new ConnectAllDrives.Request(OnlyAutoConnect: true));

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).ConnectAsync(Automatic, Arg.Any<CancellationToken>());
        await _nasConnectorMock.DidNotReceive().ConnectAsync(Manual, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_DoNothing_WhenNoDriveOptsIntoTheUnattendedPass()
    {
        // Arrange
        HaveDrives(Manual);

        // Act
        Result result = await _connectAllDrives.Handle(new ConnectAllDrives.Request(OnlyAutoConnect: true));

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_SkipDrivesThatAreAlreadyMounted()
    {
        // Arrange
        HaveDrives(Automatic, Manual);

        _nasConnectorMock.GetConnectedLetters().Returns(["Z"]);

        // Act
        await _connectAllDrives.Handle();

        // Assert
        await _nasConnectorMock.DidNotReceive().ConnectAsync(Automatic, Arg.Any<CancellationToken>());
        await _nasConnectorMock.Received(1).ConnectAsync(Manual, Arg.Any<CancellationToken>());
    }
}

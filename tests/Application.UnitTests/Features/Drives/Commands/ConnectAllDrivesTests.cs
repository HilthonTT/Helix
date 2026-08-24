using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
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
    private readonly IDriveMonitor _driveMonitorMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly IDateTimeProvider _dateTimeProviderMock;

    public ConnectAllDrivesTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _driveMonitorMock = Substitute.For<IDriveMonitor>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        // Nothing is mounted, so every drive is a candidate before the flag is applied.
        _nasConnectorMock.GetConnectedLetters().Returns([]);
        _nasConnectorMock.ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _connectAllDrives = new(
            _driveRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _driveMonitorMock,
            _dateTimeProviderMock);
    }

    private static Drive Automatic { get; } =
        Drive.Create(UserId, "Z", "nas.local", "Media", "user", "password", autoConnect: true);

    private static Drive Manual { get; } =
        Drive.Create(UserId, "Y", "nas.local", "Backup", "user", "password", autoConnect: false);

    private void HaveDrives(params Drive[] drives) =>
        _driveRepositoryMock.GetAsync(UserId, Arg.Any<CancellationToken>())
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

    /// <summary>
    /// This is the pass that runs as the dashboard opens, moments after the watchdog
    /// seeded its baseline from a machine with nothing mounted. Unannounced, every drive
    /// it brings up reads as a fresh connection and the tray fires a toast for each.
    /// </summary>
    [Fact]
    public async Task Handle_Should_TellTheMonitorTheMountsAreItsOwn()
    {
        // Arrange
        HaveDrives(Automatic, Manual);

        // Act
        await _connectAllDrives.Handle();

        // Assert
        _driveMonitorMock.Received(1).Suppress(
            Arg.Is<IEnumerable<string>>(letters => letters.OrderBy(l => l).SequenceEqual(new[] { "Y", "Z" })));
    }

    [Fact]
    public async Task Handle_Should_ReleaseTheSuppressionWhenItIsDone()
    {
        // Arrange
        HaveDrives(Automatic);

        var suppression = Substitute.For<IDisposable>();
        _driveMonitorMock.Suppress(Arg.Any<IEnumerable<string>>()).Returns(suppression);

        // Act
        await _connectAllDrives.Handle();

        // Assert
        suppression.Received(1).Dispose();
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

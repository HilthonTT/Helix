using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

/// <summary>
/// Covers the path behind the reported "I disconnected everything and it told me the
/// drives reconnected": the monitor has to be told the drop is Helix's own doing, or the
/// watchdog reads thirteen letters vanishing at once as the NAS falling over and puts
/// every one of them back.
/// </summary>
public class DisconnectAllDrivesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly DisconnectAllDrives _disconnectAllDrives;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IDriveMonitor _driveMonitorMock;

    public DisconnectAllDrivesTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _driveMonitorMock = Substitute.For<IDriveMonitor>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _nasConnectorMock.DisconnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _disconnectAllDrives = new(
            _driveRepositoryMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _driveMonitorMock);
    }

    private static Drive Media { get; } =
        Drive.Create(UserId, "Z", "nas.local", "Media", "user", "password");

    private static Drive Backup { get; } =
        Drive.Create(UserId, "Y", "nas.local", "Backup", "user", "password");

    private void HaveDrives(params Drive[] drives) =>
        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([.. drives]);

    private void Mounted(params string[] letters) =>
        _nasConnectorMock.GetConnectedLetters()
            .Returns(new HashSet<string>(letters, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task Handle_Should_DisconnectEveryMountedDrive()
    {
        // Arrange
        HaveDrives(Media, Backup);
        Mounted("Z", "Y");

        // Act
        Result result = await _disconnectAllDrives.Handle();

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).DisconnectAsync(Media, Arg.Any<CancellationToken>());
        await _nasConnectorMock.Received(1).DisconnectAsync(Backup, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_TellTheMonitorTheDropIsDeliberate()
    {
        // Arrange
        HaveDrives(Media, Backup);
        Mounted("Z", "Y");

        // Act
        await _disconnectAllDrives.Handle();

        // Assert
        _driveMonitorMock.Received(1).Suppress(
            Arg.Is<IEnumerable<string>>(letters => letters.OrderBy(l => l).SequenceEqual(new[] { "Y", "Z" })));
    }

    /// <summary>
    /// The suppression has to be in place before anything is unmounted. Filed afterwards
    /// it would lose the race against a poll landing mid-batch, which is the one that
    /// produces the spurious reconnect.
    /// </summary>
    [Fact]
    public async Task Handle_Should_SuppressBeforeItUnmountsAnything()
    {
        // Arrange
        HaveDrives(Media);
        Mounted("Z");

        // Act
        await _disconnectAllDrives.Handle();

        // Assert
        Received.InOrder(() =>
        {
            _driveMonitorMock.Suppress(Arg.Any<IEnumerable<string>>());
            _nasConnectorMock.DisconnectAsync(Media, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_Should_ReleaseTheSuppressionWhenItIsDone()
    {
        // Arrange
        HaveDrives(Media);
        Mounted("Z");

        var suppression = Substitute.For<IDisposable>();
        _driveMonitorMock.Suppress(Arg.Any<IEnumerable<string>>()).Returns(suppression);

        // Act
        await _disconnectAllDrives.Handle();

        // Assert — left held, the drive could never be reported as dropping again.
        suppression.Received(1).Dispose();
    }

    [Fact]
    public async Task Handle_Should_ReleaseTheSuppressionWhenADisconnectFails()
    {
        // Arrange
        HaveDrives(Media);
        Mounted("Z");

        var suppression = Substitute.For<IDisposable>();
        _driveMonitorMock.Suppress(Arg.Any<IEnumerable<string>>()).Returns(suppression);

        _nasConnectorMock.DisconnectAsync(Media, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToDisconnect("The device is not currently connected.")));

        // Act
        Result result = await _disconnectAllDrives.Handle();

        // Assert
        result.IsFailure.Should().BeTrue();

        suppression.Received(1).Dispose();
    }

    [Fact]
    public async Task Handle_Should_SayNothingToTheMonitor_WhenNothingIsMounted()
    {
        // Arrange
        HaveDrives(Media, Backup);
        Mounted();

        // Act
        Result result = await _disconnectAllDrives.Handle();

        // Assert
        result.IsSuccess.Should().BeTrue();

        _driveMonitorMock.DidNotReceive().Suppress(Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenNobodyIsSignedIn()
    {
        // Arrange
        _loggedInUserMock.IsLoggedIn.Returns(false);

        // Act
        Result result = await _disconnectAllDrives.Handle();

        // Assert
        result.IsFailure.Should().BeTrue();

        await _nasConnectorMock.DidNotReceive().DisconnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }
}

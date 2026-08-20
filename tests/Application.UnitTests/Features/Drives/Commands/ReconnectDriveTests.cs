using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public class ReconnectDriveTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly ReconnectDrive _reconnectDrive;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly IAuditlogRepository _auditlogRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IDateTimeProvider _dateTimeProviderMock;

    private readonly Drive _drive;

    public ReconnectDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _auditlogRepositoryMock = Substitute.For<IAuditlogRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

        _dateTimeProviderMock.UtcNow.Returns(Now);

        _reconnectDrive = new(
            _driveRepositoryMock,
            _auditlogRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _dateTimeProviderMock);

        _drive = Drive.Create(UserId, "Z", "192.168.0.1", "Media Vault", "Username", "Password");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        // Tracked, because a successful reconnect stamps LastConnectedOnUtc on the drive.
        _driveRepositoryMock.GetByIdAsync(_drive.Id).Returns(_drive);
    }

    /// <summary>
    /// Captures the entries as they are written. Asserted on by action and entity rather
    /// than by prose — the sentence no longer exists at this layer, which is the point of
    /// the structured log.
    /// </summary>
    private List<Auditlog> CapturedEntries()
    {
        List<Auditlog> entries = [];

        _auditlogRepositoryMock
            .When(r => r.Insert(Arg.Any<Auditlog>()))
            .Do(call => entries.Add(call.Arg<Auditlog>()));

        return entries;
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotLoggedIn()
    {
        // Arrange
        _loggedInUserMock.IsLoggedIn.Returns(false);

        // Act
        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert
        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenDriveBelongsToAnotherUser()
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(Guid.NewGuid());

        // Act
        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert
        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenDriveIsNotFound()
    {
        // Arrange
        var missing = Guid.NewGuid();
        _driveRepositoryMock.GetByIdAsync(missing).Returns((Drive?)null);

        // Act
        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(missing, true));

        // Assert
        result.Error.Should().Be(DriveErrors.NotFound(missing));
    }

    [Fact]
    public async Task Handle_Should_RecordTheDrop_WithoutReconnecting_WhenAutoConnectIsOff()
    {
        // Arrange
        List<Auditlog> entries = CapturedEntries();

        // Act
        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: false));

        // Assert
        result.IsSuccess.Should().BeTrue();
        entries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.DriveDisconnected);

        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_RecordTheDropAndTheRecovery_WhenReconnectSucceeds()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        List<Auditlog> entries = CapturedEntries();

        // Act
        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert
        result.IsSuccess.Should().BeTrue();
        entries.Should().HaveCount(2);
        entries[0].Action.Should().Be(AuditAction.DriveDisconnected);
        entries[1].Action.Should().Be(AuditAction.DriveReconnected);
    }

    [Fact]
    public async Task Handle_Should_StampTheDrive_WhenReconnectSucceeds()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        // Act
        await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert — this is what lets the dashboard say when a drive was last reachable.
        _drive.LastConnectedOnUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_Should_NotStampTheDrive_WhenReconnectFails()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("Still down.")));

        // Act
        await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert
        _drive.LastConnectedOnUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_RecordTheFailure_OnTheFirstFailedAttempt()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("The network path was not found.")));

        List<Auditlog> entries = CapturedEntries();

        // Act
        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert
        result.IsFailure.Should().BeTrue();
        entries.Should().HaveCount(2);
        entries[1].Action.Should().Be(AuditAction.DriveReconnectFailed);

        // The reason is carried as detail, so the rendered sentence can name it in any
        // language without the reason itself having to be translated.
        entries[1].Detail.Should().Be("The network path was not found.");
    }

    [Fact]
    public async Task Handle_Should_StaySilent_WhenARetryFails()
    {
        // Arrange — retries carry RecordDrop: false. Without this the audit log fills
        // with an entry every few seconds for as long as the NAS stays down.
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("Still down.")));

        List<Auditlog> entries = CapturedEntries();

        // Act
        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: true, RecordDrop: false));

        // Assert
        result.IsFailure.Should().BeTrue();
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_RecordTheRecovery_WhenARetrySucceeds()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        List<Auditlog> entries = CapturedEntries();

        // Act
        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: true, RecordDrop: false));

        // Assert
        result.IsSuccess.Should().BeTrue();
        entries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.DriveReconnected);
    }

    [Fact]
    public async Task Handle_Should_NameTheDriveInTheLog()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        List<Auditlog> entries = CapturedEntries();

        // Act
        await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert — the log is the only trace of what happened while the app was hidden,
        // so every entry has to identify which drive it is talking about. The name and
        // letter are copied, not looked up, so a later rename cannot rewrite history.
        entries.Should().AllSatisfy(entry =>
        {
            entry.EntityId.Should().Be(_drive.Id);
            entry.EntityName.Should().Be("Media Vault");
            entry.EntityLetter.Should().Be("Z");
        });
    }
}

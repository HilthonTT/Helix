using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public class ReconnectDriveTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly ReconnectDrive _reconnectDrive;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly IAuditlogRepository _auditlogRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;

    private readonly Drive _drive;

    public ReconnectDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _auditlogRepositoryMock = Substitute.For<IAuditlogRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();

        _reconnectDrive = new(
            _driveRepositoryMock,
            _auditlogRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock);

        _drive = Drive.Create(UserId, "Z", "192.168.0.1", "Media Vault", "Username", "Password");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsNoTrackingAsync(_drive.Id).Returns(_drive);
    }

    private List<string> CapturedMessages()
    {
        List<string> messages = [];

        _auditlogRepositoryMock
            .When(r => r.Insert(Arg.Any<Auditlog>()))
            .Do(call => messages.Add(call.Arg<Auditlog>().Message));

        return messages;
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
        _driveRepositoryMock.GetByIdAsNoTrackingAsync(missing).Returns((Drive?)null);

        // Act
        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(missing, true));

        // Assert
        result.Error.Should().Be(DriveErrors.NotFound(missing));
    }

    [Fact]
    public async Task Handle_Should_RecordTheDrop_WithoutReconnecting_WhenAutoConnectIsOff()
    {
        // Arrange
        List<string> messages = CapturedMessages();

        // Act
        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: false));

        // Assert
        result.IsSuccess.Should().BeTrue();
        messages.Should().ContainSingle().Which.Should().Contain("lost its connection");

        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_RecordTheDropAndTheRecovery_WhenReconnectSucceeds()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        List<string> messages = CapturedMessages();

        // Act
        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert
        result.IsSuccess.Should().BeTrue();
        messages.Should().HaveCount(2);
        messages[0].Should().Contain("lost its connection");
        messages[1].Should().Contain("reconnected automatically");
    }

    [Fact]
    public async Task Handle_Should_RecordTheFailure_OnTheFirstFailedAttempt()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("The network path was not found.")));

        List<string> messages = CapturedMessages();

        // Act
        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert
        result.IsFailure.Should().BeTrue();
        messages.Should().HaveCount(2);
        messages[1].Should().Contain("could not be reconnected")
            .And.Contain("The network path was not found.");
    }

    [Fact]
    public async Task Handle_Should_StaySilent_WhenARetryFails()
    {
        // Arrange — retries carry RecordDrop: false. Without this the audit log fills
        // with an entry every few seconds for as long as the NAS stays down.
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("Still down.")));

        List<string> messages = CapturedMessages();

        // Act
        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: true, RecordDrop: false));

        // Assert
        result.IsFailure.Should().BeTrue();
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_RecordTheRecovery_WhenARetrySucceeds()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        List<string> messages = CapturedMessages();

        // Act
        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: true, RecordDrop: false));

        // Assert
        result.IsSuccess.Should().BeTrue();
        messages.Should().ContainSingle().Which.Should().Contain("reconnected automatically");
    }

    [Fact]
    public async Task Handle_Should_NameTheDriveInTheLog()
    {
        // Arrange
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        List<string> messages = CapturedMessages();

        // Act
        await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        // Assert — the log is the only trace of what happened while the app was hidden,
        // so it has to identify which drive it is talking about.
        messages.Should().AllSatisfy(m => m.Should().Contain("Media Vault").And.Contain("(Z:)"));
    }
}

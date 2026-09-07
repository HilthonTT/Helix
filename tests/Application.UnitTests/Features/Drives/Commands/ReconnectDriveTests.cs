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
    private readonly IHostReachability _hostReachabilityMock;
    private readonly IDateTimeProvider _dateTimeProviderMock;

    private readonly Drive _drive;

    public ReconnectDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _auditlogRepositoryMock = Substitute.For<IAuditlogRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _hostReachabilityMock = Substitute.For<IHostReachability>();
        _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

        _hostReachabilityMock
            .IsReachableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _dateTimeProviderMock.UtcNow.Returns(Now);

        _reconnectDrive = new(
            _driveRepositoryMock,
            _auditlogRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _hostReachabilityMock,
            _dateTimeProviderMock);

        _drive = Drive.Create(UserId, "Z", "192.168.0.1", "Media Vault", "Username", "Password");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(_drive.Id).Returns(_drive);
    }

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
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenDriveBelongsToAnotherUser()
    {
        _loggedInUserMock.UserId.Returns(Guid.NewGuid());

        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenDriveIsNotFound()
    {
        var missing = Guid.NewGuid();
        _driveRepositoryMock.GetByIdAsync(missing).Returns((Drive?)null);

        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(missing, true));

        result.Error.Should().Be(DriveErrors.NotFound(missing));
    }

    [Fact]
    public async Task Handle_Should_RecordTheDrop_WithoutReconnecting_WhenAutoConnectIsOff()
    {
        List<Auditlog> entries = CapturedEntries();

        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: false));

        result.IsSuccess.Should().BeTrue();
        entries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.DriveDisconnected);

        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_RecordTheDropAndTheRecovery_WhenReconnectSucceeds()
    {
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        List<Auditlog> entries = CapturedEntries();

        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        result.IsSuccess.Should().BeTrue();
        entries.Should().HaveCount(2);
        entries[0].Action.Should().Be(AuditAction.DriveDisconnected);
        entries[1].Action.Should().Be(AuditAction.DriveReconnected);
    }

    [Fact]
    public async Task Handle_Should_StampTheDrive_WhenReconnectSucceeds()
    {
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        _drive.LastConnectedOnUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_Should_NotStampTheDrive_WhenReconnectFails()
    {
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("Still down.")));

        await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        _drive.LastConnectedOnUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_RecordTheFailure_OnTheFirstFailedAttempt()
    {
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("The network path was not found.")));

        List<Auditlog> entries = CapturedEntries();

        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        result.IsFailure.Should().BeTrue();
        entries.Should().HaveCount(2);
        entries[1].Action.Should().Be(AuditAction.DriveReconnectFailed);

        entries[1].Detail.Should().Be("The network path was not found.");
    }

    [Fact]
    public async Task Handle_Should_StaySilent_WhenARetryFails()
    {
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("Still down.")));

        List<Auditlog> entries = CapturedEntries();

        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: true, RecordDrop: false));

        result.IsFailure.Should().BeTrue();
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_RecordTheRecovery_WhenARetrySucceeds()
    {
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        List<Auditlog> entries = CapturedEntries();

        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: true, RecordDrop: false));

        result.IsSuccess.Should().BeTrue();
        entries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.DriveReconnected);
    }

    [Fact]
    public async Task Handle_Should_NotTouchTheShare_WhenTheHostIsUnreachable()
    {
        _hostReachabilityMock
            .IsReachableAsync(_drive.Host, Arg.Any<CancellationToken>())
            .Returns(false);

        Result result = await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        result.Error.Should().Be(DriveErrors.HostUnreachable(_drive.Host));

        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
        _drive.LastConnectedOnUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_SayTheHostWasUnreachable_RatherThanThatTheShareRefused()
    {
        _hostReachabilityMock
            .IsReachableAsync(_drive.Host, Arg.Any<CancellationToken>())
            .Returns(false);

        List<Auditlog> entries = CapturedEntries();

        await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        entries.Should().HaveCount(2);
        entries[1].Action.Should().Be(AuditAction.DriveReconnectFailed);
        entries[1].Detail.Should().Contain(_drive.Host);
    }

    [Fact]
    public async Task Handle_Should_StaySilent_WhenARetryFindsTheHostStillUnreachable()
    {
        _hostReachabilityMock
            .IsReachableAsync(_drive.Host, Arg.Any<CancellationToken>())
            .Returns(false);

        List<Auditlog> entries = CapturedEntries();

        Result result = await _reconnectDrive.Handle(
            new ReconnectDrive.Request(_drive.Id, AttemptReconnect: true, RecordDrop: false));

        result.Error.Code.Should().Be(DriveErrors.HostUnreachableCode);
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_NotProbeTheHost_WhenOnlyRecordingTheDrop()
    {
        List<Auditlog> entries = CapturedEntries();

        await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, AttemptReconnect: false));

        await _hostReachabilityMock.DidNotReceive()
            .IsReachableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        entries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.DriveDisconnected);
    }

    [Fact]
    public async Task Handle_Should_NameTheDriveInTheLog()
    {
        _nasConnectorMock.ConnectAsync(_drive, Arg.Any<CancellationToken>()).Returns(Result.Success());

        List<Auditlog> entries = CapturedEntries();

        await _reconnectDrive.Handle(new ReconnectDrive.Request(_drive.Id, true));

        entries.Should().AllSatisfy(entry =>
        {
            entry.EntityId.Should().Be(_drive.Id);
            entry.EntityName.Should().Be("Media Vault");
            entry.EntityLetter.Should().Be("Z");
        });
    }
}

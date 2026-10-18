using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Features.Drives.Contracts;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Queries;

public sealed class GetDriveReliabilityTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly IDriveRepository _driveRepositoryMock = Substitute.For<IDriveRepository>();
    private readonly IAuditlogRepository _auditlogRepositoryMock = Substitute.For<IAuditlogRepository>();
    private readonly ILoggedInUser _loggedInUserMock = Substitute.For<ILoggedInUser>();
    private readonly IDateTimeProvider _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

    private readonly Drive _media = Drive.Create(UserId, "Z", "192.168.0.1", "Media", "user", "password");

    private readonly GetDriveReliability _getDriveReliability;

    public GetDriveReliabilityTests()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);
        _dateTimeProviderMock.UtcNow.Returns(Now);

        _driveRepositoryMock.GetByIdAsNoTrackingAsync(_media.Id, Arg.Any<CancellationToken>()).Returns(_media);

        _getDriveReliability = new(_driveRepositoryMock, _auditlogRepositoryMock, _loggedInUserMock, _dateTimeProviderMock);
    }

    private Auditlog Row(AuditAction action, DateTime atUtc)
    {
        var row = Auditlog.ForDrive(UserId, action, _media.Id, _media.Name, _media.Letter);
        row.CreatedOnUtc = atUtc;

        return row;
    }

    private void GivenHistory(params Auditlog[] rows) =>
        _auditlogRepositoryMock
            .GetForEntitySinceAsNoTrackingAsync(
                UserId,
                _media.Id,
                Now.AddDays(-GetDriveReliability.WindowDays),
                Arg.Any<CancellationToken>())
            .Returns([.. rows]);

    [Fact]
    public async Task Handle_Should_CountDropsAndTheTypicalRecovery()
    {
        DateTime tenDaysAgo = Now.AddDays(-10);
        DateTime twoDaysAgo = Now.AddDays(-2);
        DateTime anHourAgo = Now.AddHours(-1);

        GivenHistory(
            Row(AuditAction.DriveDisconnected, tenDaysAgo),
            Row(AuditAction.DriveReconnected, tenDaysAgo.AddSeconds(30)),
            Row(AuditAction.DriveDisconnected, twoDaysAgo),
            Row(AuditAction.DriveReconnectFailed, twoDaysAgo.AddSeconds(5)),
            Row(AuditAction.DriveReconnected, twoDaysAgo.AddSeconds(90)),
            Row(AuditAction.DriveDisconnected, anHourAgo),
            Row(AuditAction.DriveReconnected, anHourAgo.AddSeconds(40)));

        Result<DriveReliability> result = await _getDriveReliability.Handle(new GetDriveReliability.Request(_media.Id));

        result.Value.Drops.Should().Be(3);
        result.Value.DropsLastWeek.Should().Be(2);
        result.Value.Recoveries.Should().Be(3);
        result.Value.FailedReconnects.Should().Be(1);
        result.Value.TypicalRecovery.Should().Be(TimeSpan.FromSeconds(40));
        result.Value.LastDropOnUtc.Should().Be(anHourAgo);
    }

    [Fact]
    public async Task Handle_Should_NotCountAReconnectThatFollowedNoDrop()
    {
        GivenHistory(
            Row(AuditAction.DriveReconnected, Now.AddDays(-3)),
            Row(AuditAction.DriveDisconnected, Now.AddDays(-1)));

        Result<DriveReliability> result = await _getDriveReliability.Handle(new GetDriveReliability.Request(_media.Id));

        result.Value.Drops.Should().Be(1);
        result.Value.Recoveries.Should().Be(0);
        result.Value.TypicalRecovery.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_ReportNothing_ForADriveThatNeverDropped()
    {
        GivenHistory(Row(AuditAction.DriveUpdated, Now.AddDays(-1)));

        Result<DriveReliability> result = await _getDriveReliability.Handle(new GetDriveReliability.Request(_media.Id));

        result.Value.HasDropped.Should().BeFalse();
        result.Value.LastDropOnUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_RefuseAnotherUsersDrive()
    {
        Drive theirs = Drive.Create(Guid.NewGuid(), "Y", "192.168.0.1", "Theirs", "user", "password");
        _driveRepositoryMock.GetByIdAsNoTrackingAsync(theirs.Id, Arg.Any<CancellationToken>()).Returns(theirs);

        Result<DriveReliability> result = await _getDriveReliability.Handle(new GetDriveReliability.Request(theirs.Id));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }
}

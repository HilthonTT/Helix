using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Features.Drives.Contracts;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Queries;

public sealed class GetEstateReliabilityTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly IDriveRepository _driveRepositoryMock = Substitute.For<IDriveRepository>();
    private readonly IAuditlogRepository _auditlogRepositoryMock = Substitute.For<IAuditlogRepository>();
    private readonly ILoggedInUser _loggedInUserMock = Substitute.For<ILoggedInUser>();
    private readonly IDateTimeProvider _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

    private readonly Drive _media = Drive.Create(UserId, "Z", "192.168.0.1", "Media", "user", "password");
    private readonly Drive _backups = Drive.Create(UserId, "Y", "192.168.0.1", "Backups", "user", "password");
    private readonly Drive _photos = Drive.Create(UserId, "X", "192.168.0.1", "Photos", "user", "password");

    private readonly GetEstateReliability _handler;

    public GetEstateReliabilityTests()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);
        _dateTimeProviderMock.UtcNow.Returns(Now);

        _driveRepositoryMock
            .GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([_media, _backups, _photos]);

        _handler = new(_driveRepositoryMock, _auditlogRepositoryMock, _loggedInUserMock, _dateTimeProviderMock);
    }

    private static Auditlog Row(Drive drive, AuditAction action, DateTime atUtc)
    {
        var row = Auditlog.ForDrive(UserId, action, drive.Id, drive.Name, drive.Letter);
        row.CreatedOnUtc = atUtc;

        return row;
    }

    private void GivenHistory(params Auditlog[] rows) =>
        _auditlogRepositoryMock
            .GetSinceAsNoTrackingAsync(
                UserId,
                Now.AddDays(-GetEstateReliability.WindowDays),
                Arg.Any<CancellationToken>())
            .Returns([.. rows]);

    [Fact]
    public async Task Handle_Should_SummarizeEveryDrive_MostDropsFirst()
    {
        GivenHistory(
            Row(_backups, AuditAction.DriveDisconnected, Now.AddDays(-3)),
            Row(_backups, AuditAction.DriveReconnected, Now.AddDays(-3).AddSeconds(20)),
            Row(_media, AuditAction.DriveDisconnected, Now.AddDays(-2)),
            Row(_media, AuditAction.DriveReconnected, Now.AddDays(-2).AddSeconds(40)),
            Row(_media, AuditAction.DriveDisconnected, Now.AddDays(-1)),
            Row(_media, AuditAction.DriveReconnected, Now.AddDays(-1).AddSeconds(60)));

        Result<List<DriveReliabilityEntry>> result = await _handler.Handle();

        result.Value.Select(e => e.Letter).Should().Equal("Z", "Y", "X");
        result.Value[0].Reliability.Drops.Should().Be(2);
        result.Value[0].Reliability.TypicalRecovery.Should().Be(TimeSpan.FromSeconds(50));
        result.Value[1].Reliability.Drops.Should().Be(1);
        result.Value[2].Reliability.HasDropped.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_KeepOneDrivesHistoryOutOfAnothers()
    {
        GivenHistory(
            Row(_media, AuditAction.DriveDisconnected, Now.AddDays(-2)),
            Row(_backups, AuditAction.DriveReconnected, Now.AddDays(-2).AddSeconds(10)));

        Result<List<DriveReliabilityEntry>> result = await _handler.Handle();

        DriveReliabilityEntry media = result.Value.Single(e => e.DriveId == _media.Id);
        media.Reliability.Recoveries.Should().Be(0);
        media.Reliability.TypicalRecovery.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_AskForNothing_WhenThereAreNoDrives()
    {
        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);

        Result<List<DriveReliabilityEntry>> result = await _handler.Handle();

        result.Value.Should().BeEmpty();
        await _auditlogRepositoryMock.DidNotReceiveWithAnyArgs().GetSinceAsNoTrackingAsync(default, default);
    }

    [Fact]
    public async Task Handle_Should_Refuse_WhenSignedOut()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<List<DriveReliabilityEntry>> result = await _handler.Handle();

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }
}

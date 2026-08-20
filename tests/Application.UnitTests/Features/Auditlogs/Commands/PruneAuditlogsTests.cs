using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Features.Auditlogs.Commands;
using Helix.Domain.Auditlogs;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using NSubstitute;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Application.UnitTests.Features.Auditlogs.Commands;

public sealed class PruneAuditlogsTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly PruneAuditlogs _pruneAuditlogs;

    private readonly IAuditlogRepository _auditlogRepositoryMock;
    private readonly ISettingsRepository _settingsRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly IDateTimeProvider _dateTimeProviderMock;

    public PruneAuditlogsTests()
    {
        _auditlogRepositoryMock = Substitute.For<IAuditlogRepository>();
        _settingsRepositoryMock = Substitute.For<ISettingsRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);
        _dateTimeProviderMock.UtcNow.Returns(Now);

        _pruneAuditlogs = new(
            _auditlogRepositoryMock,
            _settingsRepositoryMock,
            _loggedInUserMock,
            _dateTimeProviderMock);
    }

    private void HaveRetention(int days) =>
        _settingsRepositoryMock.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(SettingsModel.Create(
                UserId,
                autoConnect: true,
                autoMinimize: false,
                setOnStartup: false,
                setDesktopShortcut: false,
                timerCount: 15,
                language: Language.English,
                auditlogRetentionDays: days));

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotSignedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<int> result = await _pruneAuditlogs.Handle();

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenTheUserHasNoSettings()
    {
        _settingsRepositoryMock.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((SettingsModel?)null);

        Result<int> result = await _pruneAuditlogs.Handle();

        result.Error.Should().Be(SettingsErrors.NotFound);
    }

    /// <summary>
    /// Zero means keep everything. It is the value existing installs are migrated to, so
    /// getting this wrong would delete history nobody asked to lose.
    /// </summary>
    [Fact]
    public async Task Handle_Should_DeleteNothing_WhenRetentionIsZero()
    {
        HaveRetention(0);

        Result<int> result = await _pruneAuditlogs.Handle();

        result.Value.Should().Be(0);

        await _auditlogRepositoryMock.DidNotReceive().DeleteOlderThanAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_DeleteEntriesOlderThanTheRetentionWindow()
    {
        HaveRetention(90);

        _auditlogRepositoryMock.DeleteOlderThanAsync(
            UserId,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>()).Returns(7);

        Result<int> result = await _pruneAuditlogs.Handle();

        result.Value.Should().Be(7);

        await _auditlogRepositoryMock.Received(1).DeleteOlderThanAsync(
            UserId,
            Now.AddDays(-90),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ScopeTheDeletionToTheSignedInUser()
    {
        HaveRetention(30);

        await _pruneAuditlogs.Handle();

        // One account trimming its own history must never touch another's.
        await _auditlogRepositoryMock.Received(1).DeleteOlderThanAsync(
            Arg.Is<Guid>(id => id == UserId),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }
}

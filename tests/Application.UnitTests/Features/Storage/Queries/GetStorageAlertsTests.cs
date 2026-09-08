using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Storage;
using Helix.Application.Features.Storage.Contracts;
using Helix.Application.Features.Storage.Queries;
using Helix.Domain.Drives;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using NSubstitute;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Application.UnitTests.Features.Storage.Queries;

public class GetStorageAlertsTests
{
    private const long Terabyte = 1024L * 1024 * 1024 * 1024;

    private static readonly Guid UserId = Guid.NewGuid();

    private readonly GetStorageAlerts _getStorageAlerts;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ISettingsRepository _settingsRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IStorageProbe _storageProbeMock;

    private readonly Drive _drive;

    public GetStorageAlertsTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _settingsRepositoryMock = Substitute.For<ISettingsRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _storageProbeMock = Substitute.For<IStorageProbe>();

        _getStorageAlerts = new(
            _driveRepositoryMock,
            _settingsRepositoryMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _storageProbeMock);

        _drive = Drive.Create(UserId, "Z", "192.168.0.1", "Media Vault", "Username", "Password");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_drive]);
        _nasConnectorMock.GetConnectedLetters().Returns(["Z"]);
        _nasConnectorMock.IsMountedFrom(Arg.Is<Drive>(d => d.Letter == "Z")).Returns(true);

        WithThreshold(10);
    }

    private void WithThreshold(int percent) =>
        _settingsRepositoryMock
            .GetByUserIdAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(SettingsModel.Create(
                UserId,
                autoConnect: true,
                autoMinimize: false,
                setOnStartup: false,
                setDesktopShortcut: false,
                timerCount: SettingsModel.DefaultTimerCount,
                language: Language.English,
                auditlogRetentionDays: SettingsModel.DefaultAuditlogRetentionDays,
                storageAlertThresholdPercent: percent));

    private void WithVolumes(params VolumeUsage[] volumes) =>
        _storageProbeMock
            .ProbeAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(volumes);

    private static VolumeUsage Volume(long total, int freePercent, params string[] letters)
    {
        long free = (long)Math.Ceiling(total * freePercent / 100.0);

        return new($"capacity:{total}", total, total - free, letters);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotLoggedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<StorageAlertReport> result = await _getStorageAlerts.Handle();

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_ReportAVolume_BelowTheThreshold()
    {
        WithVolumes(Volume(4 * Terabyte, freePercent: 4, "Z"));

        Result<StorageAlertReport> result = await _getStorageAlerts.Handle();

        result.Value.Alerts.Should().ContainSingle();
        result.Value.Alerts[0].FreePercent.Should().Be(4);
        result.Value.Alerts[0].DriveNames.Should().Equal("Media Vault");
    }

    [Fact]
    public async Task Handle_Should_ReportEveryVolumeItMeasured_NotOnlyTheFullOnes()
    {
        Drive second = Drive.Create(UserId, "Y", "192.168.0.2", "Backups", "Username", "Password");

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_drive, second]);
        _nasConnectorMock.GetConnectedLetters().Returns(["Z", "Y"]);
        _nasConnectorMock.IsMountedFrom(Arg.Is<Drive>(d => d.Letter == "Z" || d.Letter == "Y")).Returns(true);

        WithVolumes(
            Volume(4 * Terabyte, freePercent: 2, "Z"),
            Volume(2 * Terabyte, freePercent: 50, "Y"));

        Result<StorageAlertReport> result = await _getStorageAlerts.Handle();

        result.Value.Alerts.Should().ContainSingle();
        result.Value.MeasuredVolumeIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_Should_SayNothing_AboutAVolumeAtTheThreshold()
    {
        WithVolumes(Volume(4 * Terabyte, freePercent: 10, "Z"));

        Result<StorageAlertReport> result = await _getStorageAlerts.Handle();

        result.Value.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_SayNothing_WhenTheWarningIsTurnedOff()
    {
        WithThreshold(0);
        WithVolumes(Volume(4 * Terabyte, freePercent: 1, "Z"));

        Result<StorageAlertReport> result = await _getStorageAlerts.Handle();

        result.Value.Alerts.Should().BeEmpty();

        await _storageProbeMock.DidNotReceive()
            .ProbeAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_MeasureOnlyTheDrivesThatAreMounted()
    {
        Drive offline = Drive.Create(UserId, "Y", "192.168.0.2", "Offsite", "Username", "Password");

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_drive, offline]);
        _nasConnectorMock.GetConnectedLetters().Returns(["Z"]);
        _nasConnectorMock.IsMountedFrom(Arg.Is<Drive>(d => d.Letter == "Z")).Returns(true);

        WithVolumes(Volume(4 * Terabyte, freePercent: 4, "Z"));

        await _getStorageAlerts.Handle();

        await _storageProbeMock.Received(1).ProbeAsync(
            Arg.Is<IReadOnlyCollection<string>>(letters => letters.SequenceEqual(new[] { "Z" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReportOnePoolOnce_NamingEveryShareOfIt()
    {
        Drive second = Drive.Create(UserId, "Y", "192.168.0.1", "Backups", "Username", "Password");

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_drive, second]);
        _nasConnectorMock.GetConnectedLetters().Returns(["Z", "Y"]);
        _nasConnectorMock.IsMountedFrom(Arg.Is<Drive>(d => d.Letter == "Z" || d.Letter == "Y")).Returns(true);

        WithVolumes(Volume(4 * Terabyte, freePercent: 2, "Z", "Y"));

        Result<StorageAlertReport> result = await _getStorageAlerts.Handle();

        result.Value.Alerts.Should().ContainSingle();
        result.Value.Alerts[0].DriveNames.Should().BeEquivalentTo("Media Vault", "Backups");
    }

    [Fact]
    public async Task Handle_Should_ReportEachVolume_Separately()
    {
        Drive second = Drive.Create(UserId, "Y", "192.168.0.2", "Backups", "Username", "Password");

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_drive, second]);
        _nasConnectorMock.GetConnectedLetters().Returns(["Z", "Y"]);
        _nasConnectorMock.IsMountedFrom(Arg.Is<Drive>(d => d.Letter == "Z" || d.Letter == "Y")).Returns(true);

        WithVolumes(
            Volume(4 * Terabyte, freePercent: 2, "Z"),
            Volume(2 * Terabyte, freePercent: 1, "Y"));

        Result<StorageAlertReport> result = await _getStorageAlerts.Handle();

        result.Value.Alerts.Should().HaveCount(2);
        result.Value.Alerts.Select(alert => alert.VolumeId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_Should_SayNothing_WhenTheUserHasNoSettingsYet()
    {
        _settingsRepositoryMock
            .GetByUserIdAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((SettingsModel?)null);

        Result<StorageAlertReport> result = await _getStorageAlerts.Handle();

        result.IsSuccess.Should().BeTrue();
        result.Value.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_FallBackToTheLetter_WhenAVolumeIsMountedByADriveThatIsGone()
    {
        WithVolumes(Volume(4 * Terabyte, freePercent: 3, "Q"));

        Result<StorageAlertReport> result = await _getStorageAlerts.Handle();

        result.Value.Alerts.Should().ContainSingle().Which.DriveNames.Should().Equal("Q");
    }
}

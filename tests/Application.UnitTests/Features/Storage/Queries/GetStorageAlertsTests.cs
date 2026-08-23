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

    /// <summary>A volume of <paramref name="total"/> bytes with <paramref name="freePercent"/> left.</summary>
    /// <remarks>
    /// Rounded up, because the handler compares a percentage the probe rounds down: take
    /// the exact share of a terabyte-sized volume and integer division lands a byte or
    /// two short, which reads back as one percent less than the test asked for.
    /// </remarks>
    private static VolumeUsage Volume(long total, int freePercent, params string[] letters)
    {
        long free = (long)Math.Ceiling(total * freePercent / 100.0);

        return new($"capacity:{total}", total, total - free, letters);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotLoggedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<List<StorageAlert>> result = await _getStorageAlerts.Handle();

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_ReportAVolume_BelowTheThreshold()
    {
        WithVolumes(Volume(4 * Terabyte, freePercent: 4, "Z"));

        Result<List<StorageAlert>> result = await _getStorageAlerts.Handle();

        result.Value.Should().ContainSingle();
        result.Value[0].FreePercent.Should().Be(4);
        result.Value[0].DriveNames.Should().Equal("Media Vault");
    }

    [Fact]
    public async Task Handle_Should_SayNothing_AboutAVolumeAtTheThreshold()
    {
        // Exactly at the threshold is not yet below it. Warning here would mean a user
        // who asks to hear at 10% hears at 10%, every time, forever.
        WithVolumes(Volume(4 * Terabyte, freePercent: 10, "Z"));

        Result<List<StorageAlert>> result = await _getStorageAlerts.Handle();

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_SayNothing_WhenTheWarningIsTurnedOff()
    {
        WithThreshold(0);
        WithVolumes(Volume(4 * Terabyte, freePercent: 1, "Z"));

        Result<List<StorageAlert>> result = await _getStorageAlerts.Handle();

        result.Value.Should().BeEmpty();

        // Nothing measured either: a volume nobody asked about is a blocking read against
        // a network share for no reason.
        await _storageProbeMock.DidNotReceive()
            .ProbeAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_MeasureOnlyTheDrivesThatAreMounted()
    {
        Drive offline = Drive.Create(UserId, "Y", "192.168.0.2", "Offsite", "Username", "Password");

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_drive, offline]);
        _nasConnectorMock.GetConnectedLetters().Returns(["Z"]);

        WithVolumes(Volume(4 * Terabyte, freePercent: 4, "Z"));

        await _getStorageAlerts.Handle();

        // A drive that is not connected has not run out of space — it has not been asked.
        await _storageProbeMock.Received(1).ProbeAsync(
            Arg.Is<IReadOnlyCollection<string>>(letters => letters.SequenceEqual(new[] { "Z" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReportOnePoolOnce_NamingEveryShareOfIt()
    {
        // The whole reason the probe answers in volumes: thirteen shares of one NAS are
        // one thing running out of room, and thirteen notifications about it would teach
        // the user to dismiss them.
        Drive second = Drive.Create(UserId, "Y", "192.168.0.1", "Backups", "Username", "Password");

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_drive, second]);
        _nasConnectorMock.GetConnectedLetters().Returns(["Z", "Y"]);

        WithVolumes(Volume(4 * Terabyte, freePercent: 2, "Z", "Y"));

        Result<List<StorageAlert>> result = await _getStorageAlerts.Handle();

        result.Value.Should().ContainSingle();
        result.Value[0].DriveNames.Should().BeEquivalentTo("Media Vault", "Backups");
    }

    [Fact]
    public async Task Handle_Should_ReportEachVolume_Separately()
    {
        Drive second = Drive.Create(UserId, "Y", "192.168.0.2", "Backups", "Username", "Password");

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_drive, second]);
        _nasConnectorMock.GetConnectedLetters().Returns(["Z", "Y"]);

        WithVolumes(
            Volume(4 * Terabyte, freePercent: 2, "Z"),
            Volume(2 * Terabyte, freePercent: 1, "Y"));

        Result<List<StorageAlert>> result = await _getStorageAlerts.Handle();

        result.Value.Should().HaveCount(2);
        result.Value.Select(alert => alert.VolumeId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_Should_SayNothing_WhenTheUserHasNoSettingsYet()
    {
        // Runs unattended, so it reports nothing rather than creating the row a signed-in
        // user would have got from opening the settings page.
        _settingsRepositoryMock
            .GetByUserIdAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((SettingsModel?)null);

        Result<List<StorageAlert>> result = await _getStorageAlerts.Handle();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_FallBackToTheLetter_WhenAVolumeIsMountedByADriveThatIsGone()
    {
        // The probe answers about letters; the names come from the drive rows. If the two
        // ever disagree the warning still has to identify something.
        WithVolumes(Volume(4 * Terabyte, freePercent: 3, "Q"));

        Result<List<StorageAlert>> result = await _getStorageAlerts.Handle();

        result.Value.Should().ContainSingle().Which.DriveNames.Should().Equal("Q");
    }
}

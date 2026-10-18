using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Security;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Schedules;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public sealed class VaultTests : IDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();

    private const string Passphrase = "correct horse";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"helix-vault-tests-{Guid.NewGuid():N}");

    private readonly IDriveRepository _driveRepositoryMock = Substitute.For<IDriveRepository>();
    private readonly IDriveGroupRepository _driveGroupRepositoryMock = Substitute.For<IDriveGroupRepository>();
    private readonly IScheduleRepository _scheduleRepositoryMock = Substitute.For<IScheduleRepository>();
    private readonly IUnitOfWork _unitOfWorkMock = Substitute.For<IUnitOfWork>();
    private readonly ILoggedInUser _loggedInUserMock = Substitute.For<ILoggedInUser>();
    private readonly IVaultCipher _vaultCipherMock = Substitute.For<IVaultCipher>();
    private readonly IPassphrasePrompt _passphrasePromptMock = Substitute.For<IPassphrasePrompt>();
    private readonly IFolderPicker _folderPickerMock = Substitute.For<IFolderPicker>();
    private readonly IFilePicker _filePickerMock = Substitute.For<IFilePicker>();
    private readonly INasConnector _nasConnectorMock = Substitute.For<INasConnector>();

    private readonly Drive _media = Drive.Create(UserId, "Z", "192.168.0.1", "Media", "user", "password");
    private readonly Drive _backups = Drive.Create(UserId, "Y", "192.168.0.1", "Backups", "user", "password");

    public VaultTests()
    {
        Directory.CreateDirectory(_folder);

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _passphrasePromptMock.PromptForExportPassphraseAsync(Arg.Any<CancellationToken>()).Returns(Passphrase);
        _passphrasePromptMock.PromptForImportPassphraseAsync(Arg.Any<CancellationToken>()).Returns(Passphrase);
        _folderPickerMock.PickAsync(Arg.Any<CancellationToken>()).Returns(FolderPick.At(_folder));

        _vaultCipherMock.Encrypt(Arg.Any<string>(), Passphrase).Returns(call => call.ArgAt<string>(0));
        _vaultCipherMock.Decrypt(Arg.Any<string>(), Passphrase).Returns(call => Result.Success(call.ArgAt<string>(0)));

        _nasConnectorMock.GetConnectedLetters().Returns([]);

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);
        _driveGroupRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);
        _scheduleRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ExportDrives Exporter() => new(
        _driveRepositoryMock,
        _driveGroupRepositoryMock,
        _scheduleRepositoryMock,
        _loggedInUserMock,
        _vaultCipherMock,
        _passphrasePromptMock,
        _folderPickerMock);

    private ImportDrives Importer() => new(
        _driveRepositoryMock,
        _driveGroupRepositoryMock,
        _scheduleRepositoryMock,
        _unitOfWorkMock,
        _loggedInUserMock,
        _vaultCipherMock,
        _passphrasePromptMock,
        _filePickerMock,
        _nasConnectorMock);

    private async Task<string> ExportAsync(List<Drive> drives, List<DriveGroup> groups, List<Schedule> schedules)
    {
        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns(drives);
        _driveGroupRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns(groups);
        _scheduleRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns(schedules);

        Result result = await Exporter().Handle();
        result.IsSuccess.Should().BeTrue();

        string file = Directory.GetFiles(_folder, "*.helixvault").Single();

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);
        _driveGroupRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);
        _scheduleRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);

        return file;
    }

    private Task<Result<VaultImport>> ImportAsync(string file)
    {
        _filePickerMock.PickAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(file);

        return Importer().Handle();
    }

    private async Task<string> WriteVaultAsync(string plaintext)
    {
        string file = Path.Combine(_folder, $"{Guid.NewGuid():N}.helixvault");
        await File.WriteAllTextAsync(file, plaintext);

        return file;
    }

    [Fact]
    public async Task Import_Should_RestoreGroupsAndSchedules_ThatWereExported()
    {
        var office = DriveGroup.Create(UserId, "Office", [_media.Id, _backups.Id]);
        var connect = Schedule.Create(UserId, office.Id, ScheduleAction.Connect, new TimeOnly(7, 0), ScheduleDays.Weekdays);
        var disconnect = Schedule.Create(UserId, null, ScheduleAction.Disconnect, new TimeOnly(23, 0), ScheduleDays.Everyday);
        disconnect.Update(null, ScheduleAction.Disconnect, new TimeOnly(23, 0), ScheduleDays.Everyday, isEnabled: false);

        string file = await ExportAsync([_media, _backups], [office], [connect, disconnect]);

        var groups = new List<DriveGroup>();
        var schedules = new List<Schedule>();
        _driveGroupRepositoryMock.Insert(Arg.Do<DriveGroup>(groups.Add));
        _scheduleRepositoryMock.Insert(Arg.Do<Schedule>(schedules.Add));

        Result<VaultImport> result = await ImportAsync(file);

        result.IsSuccess.Should().BeTrue();
        result.Value.Drives.Select(d => d.Letter).Should().BeEquivalentTo(["Z", "Y"]);

        DriveGroup group = groups.Should().ContainSingle().Subject;
        group.Name.Should().Be("Office");
        group.DriveIds.Should().BeEquivalentTo(result.Value.Drives.Select(d => d.Id));

        schedules.Should().HaveCount(2);
        schedules.Should().ContainSingle(s => s.DriveGroupId == group.Id && s.TimeOfDay == new TimeOnly(7, 0) && s.IsEnabled);
        schedules.Should().ContainSingle(s => s.DriveGroupId == null && s.Disconnects && !s.IsEnabled);

        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_Should_PointGroupsAtExistingDrives_WhenTheirLettersAreAlreadyThere()
    {
        var office = DriveGroup.Create(UserId, "Office", [_media.Id]);
        string file = await ExportAsync([_media], [office], []);

        Drive existing = Drive.Create(UserId, "Z", "192.168.0.1", "Media", "user", "password");
        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([existing]);

        DriveGroup? inserted = null;
        _driveGroupRepositoryMock.Insert(Arg.Do<DriveGroup>(g => inserted = g));

        Result<VaultImport> result = await ImportAsync(file);

        result.IsSuccess.Should().BeTrue();
        result.Value.Drives.Should().BeEmpty();
        result.Value.GroupCount.Should().Be(1);
        inserted!.DriveIds.Should().Equal(existing.Id);
    }

    [Fact]
    public async Task Import_Should_SkipGroupsAndSchedulesThatAlreadyExist()
    {
        var office = DriveGroup.Create(UserId, "Office", [_media.Id]);
        var connect = Schedule.Create(UserId, office.Id, ScheduleAction.Connect, new TimeOnly(7, 0), ScheduleDays.Weekdays);
        string file = await ExportAsync([_media], [office], [connect]);

        var existingGroup = DriveGroup.Create(UserId, "office", [_media.Id]);
        var existingSchedule = Schedule.Create(UserId, existingGroup.Id, ScheduleAction.Connect, new TimeOnly(7, 0), ScheduleDays.Weekdays);

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_media]);
        _driveGroupRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([existingGroup]);
        _scheduleRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([existingSchedule]);

        Result<VaultImport> result = await ImportAsync(file);

        result.Error.Should().Be(JsonErrors.NothingToImport);
        _driveGroupRepositoryMock.DidNotReceive().Insert(Arg.Any<DriveGroup>());
        _scheduleRepositoryMock.DidNotReceive().Insert(Arg.Any<Schedule>());
    }

    [Fact]
    public async Task Import_Should_DropAScheduleWhoseGroupIsNotInTheVault()
    {
        string file = await WriteVaultAsync("""
            {"Version":2,
             "Drives":[{"Letter":"Z","Host":"192.168.0.1","Name":"Media","Username":"user","Password":"password"}],
             "Groups":[],
             "Schedules":[{"GroupName":"Gone","Action":0,"TimeOfDay":"07:00:00","Days":62}]}
            """);

        Result<VaultImport> result = await ImportAsync(file);

        result.IsSuccess.Should().BeTrue();
        result.Value.ScheduleCount.Should().Be(0);
        _scheduleRepositoryMock.DidNotReceive().Insert(Arg.Any<Schedule>());
    }

    [Fact]
    public async Task Import_Should_StillReadAVaultWrittenBeforeGroupsWereExported()
    {
        string file = await WriteVaultAsync("""
            [{"Letter":"Z","IpAddress":"192.168.0.1","Name":"Media","Username":"user","Password":"password"}]
            """);

        Result<VaultImport> result = await ImportAsync(file);

        result.IsSuccess.Should().BeTrue();
        result.Value.Drives.Should().ContainSingle(d => d.Letter == "Z" && d.Host == "192.168.0.1");
        result.Value.GroupCount.Should().Be(0);
    }

    [Fact]
    public async Task Import_Should_RefuseAVaultFromANewerVersion()
    {
        string file = await WriteVaultAsync("""
            {"Version":99,"Drives":[{"Letter":"Z","Host":"192.168.0.1","Name":"Media","Username":"user","Password":"password"}]}
            """);

        Result<VaultImport> result = await ImportAsync(file);

        result.Error.Should().Be(JsonErrors.VaultUnsupportedVersion);
    }
}

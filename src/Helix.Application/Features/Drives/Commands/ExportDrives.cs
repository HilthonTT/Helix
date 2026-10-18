using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Security;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Schedules;
using Helix.Domain.Users;
using System.Text.Json;

namespace Helix.Application.Features.Drives.Commands;

public sealed class ExportDrives(
    IDriveRepository driveRepository,
    IDriveGroupRepository driveGroupRepository,
    IScheduleRepository scheduleRepository,
    ILoggedInUser loggedInUser,
    IVaultCipher vaultCipher,
    IPassphrasePrompt passphrasePrompt,
    IFolderPicker folderPicker) : IHandler
{
    private const string FileExtension = ".helixvault";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = false,
    };

    public async Task<Result> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);
        if (drives.Count == 0)
        {
            return Result.Failure(DriveErrors.NoDrivesFound);
        }

        string? passphrase = await passphrasePrompt.PromptForExportPassphraseAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return Result.Failure(JsonErrors.PassphraseMissing);
        }

        List<DriveGroup> groups = await driveGroupRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);
        List<Schedule> schedules = await scheduleRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        VaultContents contents = Compose(drives, groups, schedules);

        string plaintext = JsonSerializer.Serialize(contents, JsonSerializerOptions);
        string vault = vaultCipher.Encrypt(plaintext, passphrase);

        FolderPick folder = await folderPicker.PickAsync(cancellationToken);
        if (!folder.IsSuccessful)
        {
            return Result.Failure(FolderPickerErrors.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(folder.Path))
        {
            return Result.Failure(FolderPickerErrors.InvalidFolderPath);
        }

        string fileName = $"helix-drives-{DateTime.UtcNow:yyyyMMdd-HHmmss}{FileExtension}";
        string filePath = Path.Combine(folder.Path, fileName);

        if (File.Exists(filePath))
        {
            return Result.Failure(FolderPickerErrors.FileAlreadyExists);
        }

        try
        {
            await File.WriteAllTextAsync(filePath, vault, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure(FolderPickerErrors.UnauthorizedFileAccess);
        }
        catch (IOException ex)
        {
            return Result.Failure(FolderPickerErrors.WriteFailed(ex.Message));
        }

        return Result.Success();
    }

    private static VaultContents Compose(List<Drive> drives, List<DriveGroup> groups, List<Schedule> schedules)
    {
        Dictionary<Guid, string> letters = drives.ToDictionary(d => d.Id, d => d.Letter);
        Dictionary<Guid, string> groupNames = groups.ToDictionary(g => g.Id, g => g.Name);

        List<DriveImportDto> exportedDrives = drives
            .Select(d => new DriveImportDto(
                d.Letter,
                d.Host,
                d.Name,
                d.Username,
                d.Password,
                d.AutoConnect,
                d.Persistent,
                d.ConnectByHostname,
                d.MacAddress,
                d.RemoteHost,
                d.HomeNetworkId,
                d.HomeNetworkName))
            .ToList();

        List<DriveGroupImportDto> exportedGroups = groups
            .Select(g => new DriveGroupImportDto(
                g.Name,
                [.. g.DriveIds.Where(letters.ContainsKey).Select(id => letters[id])]))
            .Where(g => g.Letters.Count > 0)
            .ToList();

        List<ScheduleImportDto> exportedSchedules = schedules
            .Where(s => s.DriveGroupId is null || groupNames.ContainsKey(s.DriveGroupId.Value))
            .Select(s => new ScheduleImportDto(
                s.DriveGroupId is Guid groupId ? groupNames[groupId] : null,
                s.Action,
                s.TimeOfDay,
                s.Days,
                s.IsEnabled))
            .ToList();

        return new VaultContents(VaultContents.CurrentVersion, exportedDrives, exportedGroups, exportedSchedules);
    }
}

using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Security;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Application.Features.Drives.Contracts;
using Helix.Application.Features.Schedules.Commands;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Schedules;
using Helix.Domain.Users;
using System.Text.Json;

namespace Helix.Application.Features.Drives.Commands;

public sealed class ImportDrives(
    IDriveRepository driveRepository,
    IDriveGroupRepository driveGroupRepository,
    IScheduleRepository scheduleRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    IVaultCipher vaultCipher,
    IPassphrasePrompt passphrasePrompt,
    IFilePicker filePicker,
    INasConnector nasConnector) : IHandler
{
    private const string FileExtension = ".helixvault";

    public async Task<Result<VaultImport>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<VaultImport>(AuthenticationErrors.InvalidPermissions);
        }

        string? filePath = await filePicker.PickAsync(FileExtension, cancellationToken);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result.Failure<VaultImport>(FolderPickerErrors.Cancelled);
        }

        if (!filePath.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<VaultImport>(JsonErrors.Invalid);
        }

        string? passphrase = await passphrasePrompt.PromptForImportPassphraseAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return Result.Failure<VaultImport>(JsonErrors.PassphraseMissing);
        }

        string vault;

        try
        {
            vault = await File.ReadAllTextAsync(filePath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<VaultImport>(FolderPickerErrors.ReadFailed(ex.Message));
        }

        Result<string> decryptResult = vaultCipher.Decrypt(vault, passphrase);
        if (decryptResult.IsFailure)
        {
            return Result.Failure<VaultImport>(decryptResult.Error);
        }

        VaultContents? contents = Parse(decryptResult.Value);
        if (contents is null)
        {
            return Result.Failure<VaultImport>(JsonErrors.VaultInvalidDriveData);
        }

        if (contents.Version > VaultContents.CurrentVersion)
        {
            return Result.Failure<VaultImport>(JsonErrors.VaultUnsupportedVersion);
        }

        List<DriveImportDto> dtos = contents.Drives;
        if (dtos.Count == 0)
        {
            return Result.Failure<VaultImport>(JsonErrors.VaultInvalidDriveData);
        }

        foreach (DriveImportDto dto in dtos)
        {
            if (!IsValidDto(dto))
            {
                return Result.Failure<VaultImport>(JsonErrors.VaultInvalidDriveData);
            }
        }

        List<DriveImportDto> distinct = dtos
            .GroupBy(d => d.Letter.ToUpperInvariant())
            .Select(g => g.First())
            .ToList();

        List<Drive> candidates = distinct
            .Select(d =>
            {
                var drive = Drive.Create(
                    loggedInUser.UserId,
                    d.Letter,
                    d.EffectiveHost,
                    d.Name,
                    d.Username,
                    d.Password,
                    d.AutoConnect,
                    d.Persistent,
                    d.ConnectByHostname,
                    d.Protocol,
                    d.Port);

                drive.RememberMacAddress(d.MacAddress);
                drive.PinToNetwork(d.HomeNetworkId, d.HomeNetworkName);

                if (GeneralValidation.IsValidHost(d.RemoteHost ?? string.Empty))
                {
                    drive.ReachAwayAt(d.RemoteHost);
                }

                return drive;
            })
            .ToList();

        List<Drive> existingDrives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        var taken = new HashSet<string>(existingDrives.Select(d => d.Letter), StringComparer.OrdinalIgnoreCase);

        HashSet<string> connected = nasConnector.GetConnectedLetters();

        List<Drive> newDrives = candidates
            .Where(drive => !taken.Contains(drive.Letter))
            .Where(drive => !connected.Contains(drive.Letter) || nasConnector.IsMountedFrom(drive))
            .ToList();

        Dictionary<string, Guid> driveIdsByLetter = existingDrives
            .Concat(newDrives)
            .ToDictionary(d => d.Letter, d => d.Id, StringComparer.OrdinalIgnoreCase);

        List<DriveGroup> existingGroups = await driveGroupRepository.GetAsNoTrackingAsync(
            loggedInUser.UserId,
            cancellationToken);

        List<DriveGroup> newGroups = ComposeGroups(contents.Groups ?? [], existingGroups, driveIdsByLetter);

        // The database only folds ASCII case when it keeps names unique, so "Büro" and "BÜRO"
        // can both exist; ToDictionary threw on them and failed every import.
        Dictionary<string, Guid> groupIdsByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (DriveGroup group in existingGroups.Concat(newGroups))
        {
            groupIdsByName.TryAdd(group.Name, group.Id);
        }

        List<Schedule> existingSchedules = await scheduleRepository.GetAsNoTrackingAsync(
            loggedInUser.UserId,
            cancellationToken);

        List<Schedule> newSchedules = ComposeSchedules(contents.Schedules ?? [], existingSchedules, groupIdsByName);

        if (newDrives.Count == 0 && newGroups.Count == 0 && newSchedules.Count == 0)
        {
            return Result.Failure<VaultImport>(JsonErrors.NothingToImport);
        }

        driveRepository.AddRange(newDrives);

        foreach (DriveGroup group in newGroups)
        {
            driveGroupRepository.Insert(group);
        }

        foreach (Schedule schedule in newSchedules)
        {
            scheduleRepository.Insert(schedule);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new VaultImport(newDrives, newGroups.Count, newSchedules.Count);
    }

    private static VaultContents? Parse(string plaintext)
    {
        try
        {
            if (plaintext.TrimStart().StartsWith('['))
            {
                List<DriveImportDto>? legacy = JsonSerializer.Deserialize<List<DriveImportDto>>(plaintext);

                return legacy is null ? null : new VaultContents(1, legacy);
            }

            VaultContents? contents = JsonSerializer.Deserialize<VaultContents>(plaintext);

            return contents?.Drives is null ? null : contents;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private List<DriveGroup> ComposeGroups(
        List<DriveGroupImportDto> dtos,
        List<DriveGroup> existingGroups,
        Dictionary<string, Guid> driveIdsByLetter)
    {
        var names = new HashSet<string>(existingGroups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        var groups = new List<DriveGroup>();

        foreach (DriveGroupImportDto dto in dtos)
        {
            string name = dto?.Name?.Trim() ?? string.Empty;
            if (name.Length == 0 || !names.Add(name))
            {
                continue;
            }

            List<Guid> driveIds = (dto!.Letters ?? [])
                .Where(letter => letter is not null && driveIdsByLetter.ContainsKey(letter))
                .Select(letter => driveIdsByLetter[letter])
                .ToList();

            if (driveIds.Count == 0)
            {
                continue;
            }

            groups.Add(DriveGroup.Create(loggedInUser.UserId, name, driveIds));
        }

        return groups;
    }

    private List<Schedule> ComposeSchedules(
        List<ScheduleImportDto> dtos,
        List<Schedule> existingSchedules,
        Dictionary<string, Guid> groupIdsByName)
    {
        var seen = existingSchedules
            .Select(s => (s.DriveGroupId, s.Action, s.TimeOfDay, s.Days))
            .ToHashSet();

        var schedules = new List<Schedule>();

        foreach (ScheduleImportDto dto in dtos)
        {
            if (dto is null || CreateSchedule.Validate(dto.Action, dto.Days).IsFailure)
            {
                continue;
            }

            Guid? groupId = null;
            if (dto.GroupName is not null)
            {
                if (!groupIdsByName.TryGetValue(dto.GroupName.Trim(), out Guid id))
                {
                    continue;
                }

                groupId = id;
            }

            var schedule = Schedule.Create(loggedInUser.UserId, groupId, dto.Action, dto.TimeOfDay, dto.Days);

            if (!seen.Add((schedule.DriveGroupId, schedule.Action, schedule.TimeOfDay, schedule.Days)))
            {
                continue;
            }

            if (!dto.IsEnabled)
            {
                schedule.Update(schedule.DriveGroupId, schedule.Action, schedule.TimeOfDay, schedule.Days, false);
            }

            schedules.Add(schedule);
        }

        return schedules;
    }

    private static bool IsValidDto(DriveImportDto dto)
    {
        if (dto is null)
        {
            return false;
        }

        if (!GeneralValidation.IsDriveLetter(dto.Letter))
        {
            return false;
        }

        if (!GeneralValidation.IsValidHost(dto.EffectiveHost))
        {
            return false;
        }

        if (!DriveProtocols.IsKnown(dto.Protocol) || string.IsNullOrWhiteSpace(dto.Name))
        {
            return false;
        }

        if (dto.Protocol.UsesCredentials() &&
            (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password)))
        {
            return false;
        }

        return true;
    }
}

using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Security;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using System.Text.Json;

namespace Helix.Application.Features.Drives.Commands;

public sealed class ImportDrives(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    IVaultCipher vaultCipher,
    IPassphrasePrompt passphrasePrompt,
    INasConnector nasConnector) : IHandler
{
    private const string FileExtension = ".helixvault";

    public async Task<Result<List<Drive>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Drive>>(AuthenticationErrors.InvalidPermissions);
        }

        FileResult? file = await FilePicker.Default.PickAsync(CreatePickOptions());
        if (file is null)
        {
            return Result.Failure<List<Drive>>(FolderPickerErrors.Cancelled);
        }

        if (!file.FileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<List<Drive>>(JsonErrors.Invalid);
        }

        string? passphrase = await passphrasePrompt.PromptForImportPassphraseAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return Result.Failure<List<Drive>>(JsonErrors.PassphraseMissing);
        }

        string vault;

        try
        {
            vault = await File.ReadAllTextAsync(file.FullPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<List<Drive>>(FolderPickerErrors.ReadFailed(ex.Message));
        }

        Result<string> decryptResult = vaultCipher.Decrypt(vault, passphrase);
        if (decryptResult.IsFailure)
        {
            return Result.Failure<List<Drive>>(decryptResult.Error);
        }

        List<DriveImportDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<DriveImportDto>>(decryptResult.Value);
        }
        catch (JsonException)
        {
            return Result.Failure<List<Drive>>(JsonErrors.VaultInvalidDriveData);
        }

        if (dtos is null || dtos.Count == 0)
        {
            return Result.Failure<List<Drive>>(JsonErrors.VaultInvalidDriveData);
        }

        foreach (DriveImportDto dto in dtos)
        {
            if (!IsValidDto(dto))
            {
                return Result.Failure<List<Drive>>(JsonErrors.VaultInvalidDriveData);
            }
        }

        List<DriveImportDto> distinct = dtos
            .GroupBy(d => d.Letter.ToUpperInvariant())
            .Select(g => g.First())
            .ToList();

        List<Drive> candidates = distinct
            .Select(d => Drive.Create(
                loggedInUser.UserId,
                d.Letter,
                d.EffectiveHost,
                d.Name,
                d.Username,
                d.Password,
                d.AutoConnect,
                d.Persistent,
                d.ConnectByHostname))
            .ToList();

        List<string> existingDriveLetters = await driveRepository.GetExistingDriveLettersAsync(
            candidates,
            loggedInUser.UserId,
            cancellationToken);

        var taken = new HashSet<string>(existingDriveLetters, StringComparer.OrdinalIgnoreCase);

        HashSet<string> connected = nasConnector.GetConnectedLetters();

        List<Drive> newDrives = candidates
            .Where(drive => !taken.Contains(drive.Letter))
            .Where(drive => !connected.Contains(drive.Letter) || nasConnector.IsMountedFrom(drive))
            .ToList();

        if (newDrives.Count == 0)
        {
            return Result.Failure<List<Drive>>(JsonErrors.NothingToImport);
        }

        driveRepository.AddRange(newDrives);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return newDrives;
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

        if (string.IsNullOrWhiteSpace(dto.Name) ||
            string.IsNullOrWhiteSpace(dto.Username) ||
            string.IsNullOrWhiteSpace(dto.Password))
        {
            return false;
        }

        return true;
    }

    private static PickOptions CreatePickOptions()
    {
        var fileTypes = new FilePickerFileType(
            new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.iOS, new[] { FileExtension } },
                { DevicePlatform.Android, new[] { FileExtension } },
                { DevicePlatform.WinUI, new[] { FileExtension } },
                { DevicePlatform.Tizen, new[] { FileExtension } },
                { DevicePlatform.macOS, new[] { FileExtension } },
            });

        return new PickOptions
        {
            FileTypes = fileTypes,
        };
    }
}

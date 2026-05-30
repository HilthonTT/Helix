using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Security;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;
using System.Text.Json;

namespace Helix.Application.Drives;

public sealed class ImportDrives(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    IVaultCipher vaultCipher,
    IPassphrasePrompt passphrasePrompt) : IHandler
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

        string vault = await File.ReadAllTextAsync(file.FullPath, cancellationToken);

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

        // Validate every DTO with the same rules as CreateDrive.Validate. Reject the
        // entire vault on the first invalid entry — partial imports are confusing.
        foreach (DriveImportDto dto in dtos)
        {
            if (!IsValidDto(dto))
            {
                return Result.Failure<List<Drive>>(JsonErrors.VaultInvalidDriveData);
            }
        }

        // Case-insensitive dedup so that "C" and "c" collapse to one entry, matching
        // the server-side uniqueness check in DriveRepository.IsLetterUniqueAsync.
        List<DriveImportDto> distinct = dtos
            .GroupBy(d => d.Letter.ToUpperInvariant())
            .Select(g => g.First())
            .ToList();

        // Build candidate Drive entities (with fresh Ids + correct UserId) so that
        // the existing-letter check can use the same repository method.
        List<Drive> candidates = distinct
            .Select(d => Drive.Create(loggedInUser.UserId, d.Letter, d.IpAddress, d.Name, d.Username, d.Password))
            .ToList();

        List<string> existingDriveLetters = await driveRepository.GetExistingDriveLettersAsync(
            candidates,
            loggedInUser.UserId,
            cancellationToken);

        List<Drive> newDrives = candidates
            .Where(drive => !existingDriveLetters.Contains(drive.Letter))
            .ToList();

        if (newDrives.Count == 0)
        {
            return newDrives;
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

        if (string.IsNullOrWhiteSpace(dto.Letter) || dto.Letter.Length != 1 || !char.IsLetter(dto.Letter[0]))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dto.IpAddress) || !GeneralValidation.IsValidIpAddress(dto.IpAddress))
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
            PickerTitle = "Import drives",
            FileTypes = fileTypes,
        };
    }
}

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

        // Case-insensitively, like every other comparison of a drive letter in the app.
        // A plain Contains is ordinal, and this one silently depended on Drive.Create
        // having uppercased both sides: any row that ever reached the table with a
        // lowercase letter would not match its candidate and would be imported a second
        // time, under a letter already in use.
        var taken = new HashSet<string>(existingDriveLetters, StringComparer.OrdinalIgnoreCase);

        // The same check CreateDrive makes: a letter held by a USB stick, an optical
        // drive or another account's mapping would import fine and then fail at every
        // connect with a Windows error that named no field.
        //
        // A letter that is mounted from the very share being imported is not that. It is
        // the usual state of the machine a backup is restored on: the mappings outlived
        // the records — persistent ones survive a reinstall, live ones an update — and
        // treating them as taken meant a vault of thirteen drives imported nothing until
        // every share had been disconnected by hand.
        HashSet<string> connected = nasConnector.GetConnectedLetters();

        List<Drive> newDrives = candidates
            .Where(drive => !taken.Contains(drive.Letter))
            .Where(drive => !connected.Contains(drive.Letter) || nasConnector.IsMountedFrom(drive))
            .ToList();

        // Reported rather than announced as a success: "your drives have been imported"
        // over an unchanged list is what sent one user disconnecting every share to find
        // out why.
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

        if (string.IsNullOrWhiteSpace(dto.Letter) || dto.Letter.Length != 1 || !char.IsLetter(dto.Letter[0]))
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
            PickerTitle = "Import drives",
            FileTypes = fileTypes,
        };
    }
}

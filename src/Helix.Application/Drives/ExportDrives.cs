using CommunityToolkit.Maui.Storage;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Security;
using Helix.Application.Core.Errors;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;
using System.Text.Json;

namespace Helix.Application.Drives;

public sealed class ExportDrives(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    IVaultCipher vaultCipher,
    IPassphrasePrompt passphrasePrompt) : IHandler
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

        List<DriveImportDto> exportable = drives
            .Select(d => new DriveImportDto(d.Letter, d.IpAddress, d.Name, d.Username, d.Password))
            .ToList();

        string plaintext = JsonSerializer.Serialize(exportable, JsonSerializerOptions);
        string vault = vaultCipher.Encrypt(plaintext, passphrase);

        FolderPickerResult folderResult = await FolderPicker.Default.PickAsync(cancellationToken);
        if (!folderResult.IsSuccessful)
        {
            return Result.Failure(FolderPickerErrors.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(folderResult.Folder?.Path))
        {
            return Result.Failure(FolderPickerErrors.InvalidFolderPath);
        }

        string fileName = $"helix-drives-{DateTime.UtcNow:yyyyMMdd-HHmmss}{FileExtension}";
        string filePath = Path.Combine(folderResult.Folder.Path, fileName);

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

        return Result.Success();
    }
}

using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Security;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using System.Text.Json;

namespace Helix.Application.Features.Drives.Commands;

public sealed class ExportDrives(
    IDriveRepository driveRepository,
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

        List<DriveImportDto> exportable = drives
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

        string plaintext = JsonSerializer.Serialize(exportable, JsonSerializerOptions);
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
}

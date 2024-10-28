using CommunityToolkit.Maui.Storage;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Extensions;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;
using System.Text.Json;

namespace Helix.Application.Drives;

public sealed class ExportDrives(IDbContext context, ILoggedInUser loggedInUser) : IHandler
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    public async Task<Result> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        List<Drive> drives = await context.Drives.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);
        if (drives.Count == 0)
        {
            return Result.Failure(DriveErrors.NoDrivesFound);
        }

        List<Drive> drivesWithoutUserId = drives.Select(Drive.MapWithoutUserId).ToList();

        string json = JsonSerializer.Serialize(drivesWithoutUserId, JsonSerializerOptions);

        FolderPickerResult folderResult = await FolderPicker.Default.PickAsync(cancellationToken);
        if (!folderResult.IsSuccessful)
        {
            return Result.Failure(FolderPickerErrors.Cancelled);
        }

        if (string.IsNullOrEmpty(folderResult.Folder?.Path))
        {
            return Result.Failure(FolderPickerErrors.InvalidFolderPath);
        }

        string filePath = Path.Combine(folderResult.Folder.Path, "imported-drives.json");

        if (File.Exists(filePath))
        {
            return Result.Failure(FolderPickerErrors.FileAlreadyExists);
        }

        try
        {
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure(FolderPickerErrors.UnauthorizedFileAccess);
        }

        return Result.Success();
    }
}

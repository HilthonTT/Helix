using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using System.Text.Json;

namespace Helix.Application.Drives;

public sealed class ImportDrives(
    IDbContext context, 
    ILoggedInUser loggedInUser,
    ICacheService cacheService) : IHandler
{
    private const string FileType = ".json";

    public async Task<Result<List<Drive>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Drive>>(AuthenticationErrors.InvalidPermissions);
        }

        PickOptions options = CreateOptions();
        FileResult? result = await FilePicker.Default.PickAsync(options);
        if (result is null)
        {
            return Result.Failure<List<Drive>>(FolderPickerErrors.Cancelled);
        }

        if (!result.FileName.EndsWith(FileType))
        {
            return Result.Failure<List<Drive>>(JsonErrors.Invalid);
        }

        string json = await File.ReadAllTextAsync(result.FullPath, cancellationToken);

        List<Drive>? drives;
        try
        {
            drives = JsonSerializer.Deserialize<List<Drive>>(json);
        }
        catch (JsonException)
        {
            return Result.Failure<List<Drive>>(JsonErrors.Invalid); 
        }

        if (drives is null || drives.Count == 0)
        {
            return Result.Failure<List<Drive>>(JsonErrors.Invalid);
        }

        List<Drive> distinctDrives = drives
            .GroupBy(d => d.Letter)
            .Select(g => g.First())
            .ToList();

        List<string> existingDriveLetters = await context.Drives
            .Where(d => distinctDrives.Select(dr => dr.Letter).Contains(d.Letter))
            .Select(d => d.Letter)
            .ToListAsync(cancellationToken);

        List<Drive> newDrives = distinctDrives
            .Where(drive => !existingDriveLetters.Contains(drive.Letter))
            .ToList();

        if (newDrives.Count == 0)
        {
            return newDrives;
        }

        foreach (Drive drive in newDrives)
        {
            drive.ChangeUserId(loggedInUser.UserId);
        }

        context.Drives.AddRange(newDrives);

        await context.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.Drives.All, cancellationToken);

        return newDrives;
    }

    private static PickOptions CreateOptions()
    {
        var customFileType = new FilePickerFileType(
            new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.iOS, new[] { FileType } },
                { DevicePlatform.Android, new[] { FileType } },
                { DevicePlatform.WinUI, new[] { FileType } },
                { DevicePlatform.Tizen, new[] { FileType } },
                { DevicePlatform.macOS, new[] { FileType } },
            });

        var options = new PickOptions()
        {
            PickerTitle = "Import drives",
            FileTypes = customFileType,
        };

        return options;
    }
}

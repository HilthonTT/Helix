using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Storage;
using Helix.Application.Core.Drives;
using Helix.Application.Features.Storage.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Features.Storage.Queries;

public sealed class GetStorageAlerts(
    IDriveRepository driveRepository,
    ISettingsRepository settingsRepository,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IStorageProbe storageProbe) : IHandler
{
    public async Task<Result<StorageAlertReport>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<StorageAlertReport>(AuthenticationErrors.InvalidPermissions);
        }

        SettingsModel? settings = await settingsRepository.GetByUserIdAsNoTrackingAsync(
            loggedInUser.UserId,
            cancellationToken);

        if (settings is null)
        {
            return Result.Success(StorageAlertReport.Empty);
        }

        int threshold = settings.StorageAlertThresholdPercent;
        if (threshold <= 0)
        {
            return Result.Success(StorageAlertReport.Empty);
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);
        if (drives.Count == 0)
        {
            return Result.Success(StorageAlertReport.Empty);
        }

        HashSet<string> connected = nasConnector.GetConnectedLetters();

        List<string> letters = [.. drives
            .Where(d => DriveMountBatch.IsUp(d, connected, nasConnector))
            .Select(d => d.Letter)];
        if (letters.Count == 0)
        {
            return Result.Success(StorageAlertReport.Empty);
        }

        IReadOnlyList<VolumeUsage> volumes = await storageProbe.ProbeAsync(letters, cancellationToken);

        Dictionary<string, string> namesByLetter = drives
            .GroupBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        List<StorageAlert> alerts =
        [
            .. volumes
                .Where(volume => volume.FreePercent < threshold)
                .Select(volume => new StorageAlert(
                    volume.VolumeId,
                    [.. volume.Letters.Select(letter =>
                        namesByLetter.TryGetValue(letter, out string? name) ? name : letter)],
                    volume.TotalBytes,
                    volume.FreeBytes,
                    volume.FreePercent))
        ];

        return Result.Success(new StorageAlertReport(
            alerts,
            new HashSet<string>(volumes.Select(volume => volume.VolumeId), StringComparer.Ordinal)));
    }
}

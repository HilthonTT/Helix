using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Storage;
using Helix.Application.Features.Storage.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Features.Storage.Queries;

/// <summary>
/// Reports the volumes that are running out of room, against the threshold the user set.
/// </summary>
/// <remarks>
/// The dashboard has always been able to show how full a NAS is, but only while someone
/// was looking at it — which is exactly what nobody is doing in the weeks a pool fills up.
/// This is the same measurement asked as a question that can be answered while the app is
/// in the tray.
///
/// It answers in volumes rather than drives for the reason <see cref="IStorageProbe"/>
/// exists: thirteen shares of one pool are one thing running out of space, and warning
/// about it thirteen times would train the user to dismiss the warning.
/// </remarks>
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

        // No settings row yet means the user has not reached the settings page since
        // registering. Nothing to report rather than something to fail over: this runs
        // unattended, and a background check is no place to be creating rows.
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

        // Only what is mounted. A drive that is not connected has not run out of space —
        // it has not been asked, and guessing at it would warn about a NAS that is fine.
        List<string> letters = [.. drives.Select(d => d.Letter).Where(connected.Contains)];
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

using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Storage;
using Helix.Application.Core.Drives;
using Helix.Domain.Drives;
using Helix.Domain.Storage;
using Helix.Domain.Users;

namespace Helix.Application.Features.Storage.Commands;

public sealed class RecordStorageSamples(
    IStorageSampleRepository storageSampleRepository,
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IStorageProbe storageProbe,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    public const int RetentionDays = 400;

    public async Task<Result<int>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<int>(AuthenticationErrors.InvalidPermissions);
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        HashSet<string> connected = nasConnector.GetConnectedLetters();

        List<string> letters = [.. drives
            .Where(d => DriveMountBatch.IsUp(d, connected, nasConnector))
            .Select(d => d.Letter)];
        if (letters.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<VolumeUsage> volumes = await storageProbe.ProbeAsync(letters, cancellationToken);
        if (volumes.Count == 0)
        {
            return 0;
        }

        DateTime utcNow = dateTimeProvider.UtcNow;
        var today = DateOnly.FromDateTime(utcNow);

        List<StorageSample> recordedToday = await storageSampleRepository.GetForDayAsync(
            loggedInUser.UserId,
            today,
            cancellationToken);

        Dictionary<string, StorageSample> recorded = recordedToday.ToDictionary(s => s.VolumeId, StringComparer.Ordinal);

        foreach (VolumeUsage volume in volumes)
        {
            string volumeLetters = string.Join(",", volume.Letters.Order(StringComparer.OrdinalIgnoreCase));

            if (recorded.TryGetValue(volume.VolumeId, out StorageSample? sample))
            {
                sample.Update(volume.TotalBytes, volume.FreeBytes, volumeLetters, utcNow);

                continue;
            }

            storageSampleRepository.Insert(StorageSample.Create(
                loggedInUser.UserId,
                volume.VolumeId,
                today,
                volume.TotalBytes,
                volume.FreeBytes,
                volumeLetters,
                utcNow));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await storageSampleRepository.RemoveOlderThanAsync(
            loggedInUser.UserId,
            today.AddDays(-RetentionDays),
            cancellationToken);

        return volumes.Count;
    }
}

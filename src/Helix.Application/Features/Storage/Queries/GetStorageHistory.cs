using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Storage;
using Helix.Application.Features.Storage.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Storage;
using Helix.Domain.Users;

namespace Helix.Application.Features.Storage.Queries;

public sealed class GetStorageHistory(
    IStorageSampleRepository storageSampleRepository,
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    public const int MaximumDays = 400;

    public sealed record Request(int Days);

    public async Task<Result<List<VolumeHistory>>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<VolumeHistory>>(AuthenticationErrors.InvalidPermissions);
        }

        int days = Math.Clamp(request.Days, 1, MaximumDays);
        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);

        List<StorageSample> samples = await storageSampleRepository.GetSinceAsync(
            loggedInUser.UserId,
            today.AddDays(-days),
            cancellationToken);

        if (samples.Count == 0)
        {
            return new List<VolumeHistory>();
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        Dictionary<string, string> namesByLetter = drives
            .GroupBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        DateOnly trendStart = today.AddDays(-GetStorageForecasts.WindowDays);

        List<VolumeHistory> volumes = [];

        foreach (IGrouping<string, StorageSample> volume in samples.GroupBy(s => s.VolumeId, StringComparer.Ordinal))
        {
            List<StorageSample> ordered = [.. volume.OrderBy(s => s.Day)];
            StorageSample latest = ordered[^1];

            double? bytesPerDay = StorageTrend.BytesPerDay(
                [.. ordered.Where(s => s.Day >= trendStart).Select(s => (s.Day, s.FreeBytes))]);

            volumes.Add(new VolumeHistory(
                volume.Key,
                [.. latest.Letters
                    .Split(",", StringSplitOptions.RemoveEmptyEntries)
                    .Select(letter => namesByLetter.TryGetValue(letter, out string? name)
                        ? $"{letter}: {name}"
                        : $"{letter}:")],
                latest.TotalBytes,
                latest.FreeBytes,
                [.. ordered.Select(s => new StoragePoint(s.Day, s.TotalBytes, s.FreeBytes))],
                bytesPerDay,
                StorageTrend.DaysUntilFull(latest.FreeBytes, bytesPerDay)));
        }

        return volumes
            .OrderBy(v => v.DaysUntilFull ?? int.MaxValue)
            .ThenByDescending(v => v.TotalBytes)
            .ToList();
    }
}

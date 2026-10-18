using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Storage;
using Helix.Application.Features.Storage.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Storage;
using Helix.Domain.Users;

namespace Helix.Application.Features.Storage.Queries;

public sealed class GetStorageForecasts(
    IStorageSampleRepository storageSampleRepository,
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    public const int WindowDays = 30;

    public const int StaleAfterDays = 3;

    public async Task<Result<List<StorageForecast>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<StorageForecast>>(AuthenticationErrors.InvalidPermissions);
        }

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);

        List<StorageSample> samples = await storageSampleRepository.GetSinceAsync(
            loggedInUser.UserId,
            today.AddDays(-WindowDays),
            cancellationToken);

        if (samples.Count == 0)
        {
            return new List<StorageForecast>();
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        Dictionary<string, string> namesByLetter = drives
            .GroupBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        List<StorageForecast> forecasts = [];

        foreach (IGrouping<string, StorageSample> volume in samples.GroupBy(s => s.VolumeId, StringComparer.Ordinal))
        {
            StorageSample latest = volume.MaxBy(s => s.Day)!;

            if (today.DayNumber - latest.Day.DayNumber > StaleAfterDays)
            {
                continue;
            }

            double? bytesPerDay = StorageTrend.BytesPerDay([.. volume.Select(s => (s.Day, s.FreeBytes))]);

            forecasts.Add(new StorageForecast(
                volume.Key,
                [.. latest.Letters
                    .Split(",", StringSplitOptions.RemoveEmptyEntries)
                    .Select(letter => namesByLetter.TryGetValue(letter, out string? name) ? name : letter)],
                latest.TotalBytes,
                latest.FreeBytes,
                bytesPerDay,
                StorageTrend.DaysUntilFull(latest.FreeBytes, bytesPerDay)));
        }

        return forecasts
            .OrderBy(f => f.DaysUntilFull ?? int.MaxValue)
            .ToList();
    }
}

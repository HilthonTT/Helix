using Helix.Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace Helix.Infrastructure.Storage;

internal abstract class StorageProbe : IStorageProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger _logger;

    protected StorageProbe(ILogger logger)
    {
        _logger = logger;
    }

    protected abstract string RootPathFor(string letter);

    public async Task<IReadOnlyList<VolumeUsage>> ProbeAsync(
        IReadOnlyCollection<string> driveLetters,
        CancellationToken cancellationToken = default)
    {
        if (driveLetters.Count == 0)
        {
            return [];
        }

        string[] ordered = [.. driveLetters];

        Reading?[] readings = await Task.WhenAll(
            ordered.Select(letter => MeasureAsync(letter, cancellationToken)));

        var volumes = new Dictionary<long, Volume>();

        int measured = 0;

        for (int index = 0; index < readings.Length; index++)
        {
            if (readings[index] is not Reading reading)
            {
                continue;
            }

            measured++;

            if (volumes.TryGetValue(reading.TotalBytes, out Volume? volume))
            {
                volume.FreeBytes = Math.Min(volume.FreeBytes, reading.FreeBytes);
                volume.Letters.Add(ordered[index]);

                continue;
            }

            volumes[reading.TotalBytes] = new Volume(reading.FreeBytes, [ordered[index]]);
        }

        if (volumes.Count < measured)
        {
            _logger.LogDebug(
                "Collapsed {Mounts} mounted drives to {Volumes} distinct volumes for the storage total.",
                measured,
                volumes.Count);
        }

        return
        [
            .. volumes.Select(volume => new VolumeUsage(
                $"capacity:{volume.Key}",
                volume.Key,
                Math.Max(0, volume.Key - volume.Value.FreeBytes),
                volume.Value.Letters))
        ];
    }

    private sealed record Reading(long TotalBytes, long FreeBytes);

    private sealed record Volume(long FreeBytes, List<string> Letters)
    {
        public long FreeBytes { get; set; } = FreeBytes;
    }

    private async Task<Reading?> MeasureAsync(string letter, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => Measure(letter), cancellationToken).WaitAsync(ProbeTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not measure drive {Letter}; leaving it out of the total.", letter);

            return null;
        }
    }

    protected virtual (long TotalBytes, long FreeBytes)? ReadCapacity(string rootPath)
    {
        try
        {
            var driveInfo = new DriveInfo(rootPath);

            return driveInfo.IsReady && driveInfo.TotalSize > 0
                ? (driveInfo.TotalSize, driveInfo.AvailableFreeSpace)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "The mount at {RootPath} went away while it was being measured.", rootPath);

            return null;
        }
    }

    private Reading? Measure(string letter)
    {
        string rootPath = RootPathFor(letter);

        if (ReadCapacity(rootPath) is not (long total, long free))
        {
            return null;
        }

        return new Reading(total, free);
    }
}

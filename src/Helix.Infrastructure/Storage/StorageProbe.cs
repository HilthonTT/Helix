using Helix.Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace Helix.Infrastructure.Storage;

/// <summary>
/// The platform-neutral half of the storage probe: read each mount, work out which
/// mounts are the same storage, and count that storage once.
/// </summary>
/// <remarks>
/// Two mounts are treated as one volume when they report the <em>same total size, to the
/// byte</em>. That figure is a property of the filesystem, so every share of one pool
/// reports it identically and it does not move while the probe runs.
///
/// Nothing else survived contact with a real NAS, and two more obvious ideas were tried
/// and thrown away — do not reintroduce either:
///
/// <list type="bullet">
/// <item>The <b>volume serial number</b>, via <c>GetVolumeInformation</c>. It looks like
/// the precise answer and is not one for SMB: Samba and most NAS firmware derive it per
/// share, so thirteen shares of one pool returned thirteen different serials and nothing
/// merged. Volume labels are per-share for the same reason.</item>
/// <item>The <b>free byte count</b>, as part of the key. Free space drifts continuously on
/// a NAS that anything is writing to — measured across thirteen live shares of one pool it
/// spanned about 12 MB, no two readings equal — so requiring it to match meant nothing
/// ever merged either.</item>
/// </list>
///
/// The host is deliberately not part of the identity. Including it splits one NAS added
/// twice under different spellings, once by IP and once by name, and that fails in the
/// damaging direction: over-counting, which is what this class exists to prevent.
///
/// The known cost is that two genuinely separate volumes of byte-identical size are
/// counted once. Real volume sizes are not round numbers — they fall out of disk geometry,
/// RAID layout and filesystem overhead — so an exact collision means two identically built
/// volumes, and the result is an understated total rather than a wildly overstated one.
///
/// Per-share quotas are handled correctly by the same rule: shares with different quotas
/// report different totals, so they stay separate and their allotments add up.
/// </remarks>
internal abstract class StorageProbe : IStorageProbe
{
    /// <summary>
    /// How long a single mount may take to answer before it is left out of the total.
    /// </summary>
    /// <remarks>
    /// Reading a network volume's size blocks for as long as the network takes to give
    /// up. One unreachable share must not hold up the figure for the rest.
    /// </remarks>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger _logger;

    protected StorageProbe(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>The filesystem path a drive letter is mounted at on this platform.</summary>
    protected abstract string RootPathFor(string letter);

    public async Task<IReadOnlyList<VolumeUsage>> ProbeAsync(
        IReadOnlyCollection<string> driveLetters,
        CancellationToken cancellationToken = default)
    {
        if (driveLetters.Count == 0)
        {
            return [];
        }

        Reading?[] readings = await Task.WhenAll(
            driveLetters.Select(letter => MeasureAsync(letter, cancellationToken)));

        // Keyed by total size, keeping the smallest free reading seen for it. Smallest,
        // rather than whichever arrived first, so the figure does not flicker between
        // refreshes as the drives finish probing in a different order each time.
        var freeByTotal = new Dictionary<long, long>();

        int measured = 0;

        foreach (Reading? reading in readings)
        {
            if (reading is null)
            {
                continue;
            }

            measured++;

            freeByTotal[reading.TotalBytes] = freeByTotal.TryGetValue(reading.TotalBytes, out long free)
                ? Math.Min(free, reading.FreeBytes)
                : reading.FreeBytes;
        }

        if (freeByTotal.Count < measured)
        {
            _logger.LogDebug(
                "Collapsed {Mounts} mounted drives to {Volumes} distinct volumes for the storage total.",
                measured,
                freeByTotal.Count);
        }

        return
        [
            .. freeByTotal.Select(volume => new VolumeUsage(
                $"capacity:{volume.Key}",
                volume.Key,
                Math.Max(0, volume.Key - volume.Value)))
        ];
    }

    /// <summary>One mount's raw numbers, before duplicates are collapsed.</summary>
    private sealed record Reading(long TotalBytes, long FreeBytes);

    private async Task<Reading?> MeasureAsync(string letter, CancellationToken cancellationToken)
    {
        try
        {
            // A blocking DriveInfo read cannot be cancelled, so an abandoned probe is left
            // to finish on its own; what matters is that the caller is released.
            return await Task.Run(() => Measure(letter), cancellationToken).WaitAsync(ProbeTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not measure drive {Letter}; leaving it out of the total.", letter);

            return null;
        }
    }

    /// <summary>
    /// Total and free bytes behind a mount, or null if it is not reachable.
    /// </summary>
    /// <remarks>
    /// Virtual so the deduplication above can be tested without a real NAS on the other
    /// end — that logic is the entire reason this class exists and is worth pinning down.
    /// </remarks>
    protected virtual (long TotalBytes, long FreeBytes)? ReadCapacity(string rootPath)
    {
        var driveInfo = new DriveInfo(rootPath);

        return driveInfo.IsReady && driveInfo.TotalSize > 0
            ? (driveInfo.TotalSize, driveInfo.AvailableFreeSpace)
            : null;
    }

    private Reading? Measure(string letter)
    {
        string rootPath = RootPathFor(letter);

        // Not reachable right now. Left out rather than counted as zero: a share that is
        // merely offline has not lost its capacity, and reporting it as empty would make
        // the total drop every time a drive dropped.
        if (ReadCapacity(rootPath) is not (long total, long free))
        {
            return null;
        }

        return new Reading(total, free);
    }
}

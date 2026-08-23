namespace Helix.Application.Abstractions.Storage;

/// <summary>The capacity of one distinct volume.</summary>
/// <param name="VolumeId">
/// Identifies the storage behind a mount — in practice its exact size in bytes. Two
/// drives that resolve to the same id are two shares of one volume, counted once.
/// </param>
/// <param name="Letters">
/// The mounts that turned out to be this one volume, in the order they were measured.
/// </param>
/// <remarks>
/// The letters are carried because a total is not the only thing a caller wants from a
/// volume: telling the user one is nearly full is no use unless it also says which of
/// their drives that is. The dashboard ignores them and sums the sizes as before.
/// </remarks>
public sealed record VolumeUsage(
    string VolumeId,
    long TotalBytes,
    long UsedBytes,
    IReadOnlyList<string> Letters)
{
    /// <summary>Free bytes, as the difference the caller would otherwise work out itself.</summary>
    public long FreeBytes => Math.Max(0, TotalBytes - UsedBytes);

    /// <summary>
    /// Free space as a percentage of the volume, rounded down, or 0 for a volume that
    /// reports no size at all.
    /// </summary>
    public int FreePercent => TotalBytes <= 0 ? 0 : (int)(FreeBytes * 100 / TotalBytes);
}

/// <summary>
/// Measures how much storage a set of mounted drives actually represents.
/// </summary>
/// <remarks>
/// This exists because adding up per-drive capacity is wrong, and wrong by a lot. Several
/// mapped drives are commonly several shares of one NAS — the same pool behind all of
/// them — and every one of them reports that pool's full size. A 43.2 TB NAS mapped
/// thirteen times totalled 562 TB.
///
/// So the probe answers in volumes, not in drives: it measures every mount, works out
/// which of them are the same storage, and returns one reading per distinct volume. The
/// caller sums whatever it gets back.
/// </remarks>
public interface IStorageProbe
{
    /// <summary>
    /// Measures the given drive letters and returns one entry per distinct volume.
    /// </summary>
    /// <remarks>
    /// Takes the letters alone: which server a drive was reached through deliberately
    /// plays no part in deciding what counts as one volume, because one NAS added twice
    /// under two spellings is still one NAS. Drives that are not currently reachable are
    /// left out rather than counted as empty.
    /// </remarks>
    Task<IReadOnlyList<VolumeUsage>> ProbeAsync(
        IReadOnlyCollection<string> driveLetters,
        CancellationToken cancellationToken = default);
}

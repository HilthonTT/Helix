namespace Helix.Application.Abstractions.Storage;

/// <summary>The capacity of one distinct volume.</summary>
/// <param name="VolumeId">
/// Identifies the storage behind a mount — in practice its exact size in bytes. Two
/// drives that resolve to the same id are two shares of one volume, counted once.
/// </param>
public sealed record VolumeUsage(string VolumeId, long TotalBytes, long UsedBytes);

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

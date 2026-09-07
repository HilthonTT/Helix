namespace Helix.Application.Abstractions.Storage;

public sealed record VolumeUsage(
    string VolumeId,
    long TotalBytes,
    long UsedBytes,
    IReadOnlyList<string> Letters)
{
    public long FreeBytes => Math.Max(0, TotalBytes - UsedBytes);

    public int FreePercent => TotalBytes <= 0 ? 0 : (int)(FreeBytes * 100 / TotalBytes);
}

public interface IStorageProbe
{
    Task<IReadOnlyList<VolumeUsage>> ProbeAsync(
        IReadOnlyCollection<string> driveLetters,
        CancellationToken cancellationToken = default);
}

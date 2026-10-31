namespace Helix.Application.Features.Storage.Contracts;

public sealed record StoragePoint(DateOnly Day, long TotalBytes, long FreeBytes);

public sealed record VolumeHistory(
    string VolumeId,
    IReadOnlyList<string> DriveNames,
    long TotalBytes,
    long FreeBytes,
    IReadOnlyList<StoragePoint> Points,
    double? BytesPerDay,
    int? DaysUntilFull);

namespace Helix.Application.Features.Storage.Contracts;

public sealed record StorageForecast(
    string VolumeId,
    IReadOnlyList<string> DriveNames,
    long TotalBytes,
    long FreeBytes,
    double? BytesPerDay,
    int? DaysUntilFull);

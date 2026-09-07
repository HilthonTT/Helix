namespace Helix.Application.Features.Storage.Contracts;

public sealed record StorageAlert(
    string VolumeId,
    IReadOnlyList<string> DriveNames,
    long TotalBytes,
    long FreeBytes,
    int FreePercent);

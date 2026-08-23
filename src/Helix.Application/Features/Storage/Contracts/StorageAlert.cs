namespace Helix.Application.Features.Storage.Contracts;

/// <summary>
/// One volume that has less room left than the user asked to be warned about.
/// </summary>
/// <param name="VolumeId">
/// The volume's identity as the probe reports it, stable between checks. What the caller
/// remembers so it can warn once about a volume rather than once per check.
/// </param>
/// <param name="DriveNames">
/// The drives this volume is mounted as, named as the user named them. Plural because
/// several shares of one NAS pool are one volume, and telling someone their storage is
/// nearly full without saying which of their drives it is behind is not much of a warning.
/// </param>
public sealed record StorageAlert(
    string VolumeId,
    IReadOnlyList<string> DriveNames,
    long TotalBytes,
    long FreeBytes,
    int FreePercent);

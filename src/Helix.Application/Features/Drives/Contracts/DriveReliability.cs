namespace Helix.Application.Features.Drives.Contracts;

public sealed record DriveReliability(
    int WindowDays,
    int Drops,
    int DropsLastWeek,
    int Recoveries,
    int FailedReconnects,
    TimeSpan? TypicalRecovery,
    DateTime? LastDropOnUtc)
{
    public bool HasDropped => Drops > 0;
}

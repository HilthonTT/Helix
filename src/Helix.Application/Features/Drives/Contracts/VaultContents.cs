namespace Helix.Application.Features.Drives.Contracts;

public sealed record VaultContents(
    int Version,
    List<DriveImportDto> Drives,
    List<DriveGroupImportDto>? Groups = null,
    List<ScheduleImportDto>? Schedules = null)
{
    public const int CurrentVersion = 2;
}

using Helix.Domain.Schedules;

namespace Helix.Application.Features.Schedules.Contracts;

public sealed record ScheduleRun(
    Guid ScheduleId,
    ScheduleAction Action,
    string? GroupName,
    IReadOnlyList<Guid> DriveIds,
    Result Outcome);

using Helix.Domain.Schedules;

namespace Helix.Application.Features.Drives.Contracts;

public sealed record ScheduleImportDto(
    string? GroupName,
    ScheduleAction Action,
    TimeOnly TimeOfDay,
    ScheduleDays Days,
    bool IsEnabled = true);

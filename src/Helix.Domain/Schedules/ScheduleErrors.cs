namespace Helix.Domain.Schedules;

public static class ScheduleErrors
{
    public static Error NotFound(Guid id) => Error.NotFound(
        "Schedule.NotFound",
        $"The schedule with the specified Id = '{id}' was not found.");

    public static readonly Error NoDaysSelected = Error.Problem(
        "Schedule.NoDaysSelected",
        "Choose at least one day for the schedule.");

    public static readonly Error UnknownAction = Error.Problem(
        "Schedule.UnknownAction",
        "A schedule either connects or disconnects.");
}

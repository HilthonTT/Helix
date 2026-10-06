namespace Helix.Domain.Schedules;

[Flags]
public enum ScheduleDays
{
    None = 0,

    Sunday = 1 << 0,

    Monday = 1 << 1,

    Tuesday = 1 << 2,

    Wednesday = 1 << 3,

    Thursday = 1 << 4,

    Friday = 1 << 5,

    Saturday = 1 << 6,

    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,

    Weekend = Saturday | Sunday,

    Everyday = Weekdays | Weekend,
}

public static class ScheduleDaysExtensions
{
    public static ScheduleDays ToScheduleDay(this DayOfWeek day) => (ScheduleDays)(1 << (int)day);

    public static bool Includes(this ScheduleDays days, DayOfWeek day) => (days & day.ToScheduleDay()) != 0;
}

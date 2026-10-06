namespace Helix.Domain.Schedules;

public sealed class Schedule : Entity, IAuditable
{
    private Schedule(
        Guid id,
        Guid userId,
        Guid? driveGroupId,
        ScheduleAction action,
        TimeOnly timeOfDay,
        ScheduleDays days)
        : base(id)
    {
        Ensure.NotNullOrEmpty(id, nameof(id));
        Ensure.NotNullOrEmpty(userId, nameof(userId));

        UserId = userId;
        DriveGroupId = driveGroupId;
        Action = action;
        TimeOfDay = Truncate(timeOfDay);
        Days = days & ScheduleDays.Everyday;
        IsEnabled = true;

        DateTime utcNow = DateTime.UtcNow;

        CreatedOnUtc = utcNow;
        ModifiedOnUtc = utcNow;
    }

    private Schedule()
    {
    }

    public Guid UserId { get; private set; }

    public Guid? DriveGroupId { get; private set; }

    public ScheduleAction Action { get; private set; }

    public TimeOnly TimeOfDay { get; private set; }

    public ScheduleDays Days { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTime? LastRunOnUtc { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

    public bool Disconnects => Action == ScheduleAction.Disconnect;

    public static Schedule Create(
        Guid userId,
        Guid? driveGroupId,
        ScheduleAction action,
        TimeOnly timeOfDay,
        ScheduleDays days) =>
        new(Guid.CreateVersion7(), userId, driveGroupId, action, timeOfDay, days);

    public void Update(
        Guid? driveGroupId,
        ScheduleAction action,
        TimeOnly timeOfDay,
        ScheduleDays days,
        bool isEnabled)
    {
        DriveGroupId = driveGroupId;
        Action = action;
        TimeOfDay = Truncate(timeOfDay);
        Days = days & ScheduleDays.Everyday;
        IsEnabled = isEnabled;
    }

    public void MarkRun(DateTime occurrenceUtc) => LastRunOnUtc = occurrenceUtc;

    public DateTime? DueOccurrence(DateTime utcNow, TimeZoneInfo zone, TimeSpan grace)
    {
        if (!IsEnabled || Days == ScheduleDays.None)
        {
            return null;
        }

        DateTime? occurrence = LatestOccurrence(utcNow, zone);
        if (occurrence is null)
        {
            return null;
        }

        DateTime since = ModifiedOnUtc ?? CreatedOnUtc;
        if (LastRunOnUtc > since)
        {
            since = LastRunOnUtc.Value;
        }

        if (occurrence <= since || utcNow - occurrence > grace)
        {
            return null;
        }

        return occurrence;
    }

    public DateTime? LatestOccurrence(DateTime utcNow, TimeZoneInfo zone)
    {
        DateTime localNow = DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone),
            DateTimeKind.Unspecified);

        for (int daysBack = 0; daysBack <= 7; daysBack++)
        {
            DateTime date = localNow.Date.AddDays(-daysBack);
            if (!Days.Includes(date.DayOfWeek))
            {
                continue;
            }

            DateTime local = date + TimeOfDay.ToTimeSpan();
            if (local > localNow)
            {
                continue;
            }

            return ToUtc(local, zone);
        }

        return null;
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static TimeOnly Truncate(TimeOnly time) => new(time.Hour, time.Minute);
}

using Helix.App.Resources.Languages;
using Helix.Domain.Schedules;
using System.Globalization;

namespace Helix.App.Common;

internal static class ScheduleText
{
    public static string Title(ScheduleAction action, string? groupName) => (action, groupName) switch
    {
        (ScheduleAction.Disconnect, null) => AppResources.ScheduleDisconnectAll,
        (ScheduleAction.Disconnect, _) => string.Format(AppResources.ScheduleDisconnectGroup, groupName),
        (_, null) => AppResources.ScheduleConnectAll,
        _ => string.Format(AppResources.ScheduleConnectGroup, groupName),
    };

    public static string Ran(ScheduleAction action, string? groupName) => (action, groupName) switch
    {
        (ScheduleAction.Disconnect, null) => AppResources.ScheduleRanDisconnectAll,
        (ScheduleAction.Disconnect, _) => string.Format(AppResources.ScheduleRanDisconnectGroup, groupName),
        (_, null) => AppResources.ScheduleRanConnectAll,
        _ => string.Format(AppResources.ScheduleRanConnectGroup, groupName),
    };

    public static string When(ScheduleDays days, TimeOnly time) =>
        string.Format(AppResources.ScheduleWhen, Days(days), time.ToString("t", CultureInfo.CurrentCulture));

    public static string Days(ScheduleDays days) => (days & ScheduleDays.Everyday) switch
    {
        ScheduleDays.Everyday => AppResources.ScheduleEveryDay,
        ScheduleDays.Weekdays => AppResources.ScheduleWeekdays,
        ScheduleDays.Weekend => AppResources.ScheduleWeekends,
        _ => string.Join(", ", WeekInCultureOrder()
            .Where(day => days.Includes(day))
            .Select(ShortName)),
    };

    public static IEnumerable<DayOfWeek> WeekInCultureOrder()
    {
        DayOfWeek first = CultureInfo.CurrentUICulture.DateTimeFormat.FirstDayOfWeek;

        return Enumerable.Range(0, 7).Select(offset => (DayOfWeek)(((int)first + offset) % 7));
    }

    public static string ShortName(DayOfWeek day) =>
        CultureInfo.CurrentUICulture.DateTimeFormat.GetAbbreviatedDayName(day);
}

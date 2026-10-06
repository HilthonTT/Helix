using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Schedules;

namespace Helix.App.Models;

internal sealed partial class ScheduleDisplay : ObservableObject
{
    private readonly Func<ScheduleDisplay, Task>? _onEnabledChanged;

    private bool _quiet;

    public ScheduleDisplay(Schedule schedule, string? groupName, Func<ScheduleDisplay, Task>? onEnabledChanged)
    {
        Id = schedule.Id;
        DriveGroupId = schedule.DriveGroupId;
        Action = schedule.Action;
        TimeOfDay = schedule.TimeOfDay;
        Days = schedule.Days;
        IsEnabled = schedule.IsEnabled;

        Title = ScheduleText.Title(schedule.Action, groupName);
        When = ScheduleText.When(schedule.Days, schedule.TimeOfDay);

        _onEnabledChanged = onEnabledChanged;
    }

    public Guid Id { get; }

    public Guid? DriveGroupId { get; }

    public ScheduleAction Action { get; }

    public TimeOnly TimeOfDay { get; }

    public ScheduleDays Days { get; }

    public string Title { get; }

    public string When { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public void SetEnabledQuietly(bool value)
    {
        _quiet = true;

        try
        {
            IsEnabled = value;
        }
        finally
        {
            _quiet = false;
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_onEnabledChanged is not null && !_quiet)
        {
            _ = _onEnabledChanged(this);
        }
    }
}

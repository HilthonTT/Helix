using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Schedules;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.Application.Features.DriveGroups.Queries;
using Helix.Application.Features.Schedules.Commands;
using Helix.Application.Features.Schedules.Queries;
using Helix.Domain.DriveGroups;
using Helix.Domain.Schedules;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class SchedulesViewModel : BaseViewModel
{
    private static readonly TimeSpan DefaultTime = new(8, 0, 0);

    private Dictionary<Guid, string> _groupNames = [];

    public SchedulesViewModel()
    {
        Schedules = [];
        Targets = [];
        Days = [];
        Actions = [AppResources.ScheduleActionConnect, AppResources.ScheduleActionDisconnect];
        Time = DefaultTime;

        RegisterMessages();
    }

    [ObservableProperty]
    public partial ObservableCollection<ScheduleDisplay> Schedules { get; set; }

    [ObservableProperty]
    public partial List<ScheduleTargetOption> Targets { get; set; }

    [ObservableProperty]
    public partial ScheduleTargetOption? SelectedTarget { get; set; }

    public List<string> Actions { get; }

    [ObservableProperty]
    public partial int SelectedActionIndex { get; set; }

    [ObservableProperty]
    public partial TimeSpan? Time { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<ScheduleDayOption> Days { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    public partial ScheduleDisplay? Editing { get; set; }

    public bool IsEditing => Editing is not null;

    public string EditorTitle => Editing is null
        ? AppResources.NewSchedule
        : AppResources.EditSchedule;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            Result<List<DriveGroup>> groups = await ScopedHandler.HandleAsync((GetDriveGroups h) => h.Handle());
            if (groups.IsFailure)
            {
                await DisplayErrorAsync(groups.Error);
                return;
            }

            _groupNames = groups.Value.ToDictionary(group => group.Id, group => group.Name);

            Targets =
            [
                new ScheduleTargetOption(null, AppResources.ScheduleEveryDrive),
                .. groups.Value.Select(group => new ScheduleTargetOption(group.Id, group.Name)),
            ];

            await ReloadSchedulesAsync();

            StartNewSchedule();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartNewSchedule()
    {
        Editing = null;

        SelectedActionIndex = 0;
        SelectedTarget = Targets.FirstOrDefault();
        Time = DefaultTime;

        SetDays(ScheduleDays.Weekdays);
    }

    [RelayCommand]
    private void Edit(ScheduleDisplay? schedule)
    {
        if (schedule is null)
        {
            return;
        }

        Editing = schedule;

        SelectedActionIndex = schedule.Action == ScheduleAction.Disconnect ? 1 : 0;
        SelectedTarget = Targets.FirstOrDefault(target => target.DriveGroupId == schedule.DriveGroupId)
            ?? Targets.FirstOrDefault();
        Time = schedule.TimeOfDay.ToTimeSpan();

        SetDays(schedule.Days);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ScheduleAction action = SelectedActionIndex == 1 ? ScheduleAction.Disconnect : ScheduleAction.Connect;
        Guid? groupId = SelectedTarget?.DriveGroupId;
        TimeOnly time = TimeOnly.FromTimeSpan(Time ?? DefaultTime);
        ScheduleDays days = SelectedDays();

        try
        {
            IsBusy = true;

            ScheduleDisplay? editing = Editing;

            Result result = editing is null
                ? await ScopedHandler.HandleAsync((CreateSchedule h) =>
                    h.Handle(new CreateSchedule.Request(groupId, action, time, days)))
                : await ScopedHandler.HandleAsync((UpdateSchedule h) =>
                    h.Handle(new UpdateSchedule.Request(editing.Id, groupId, action, time, days, editing.IsEnabled)));

            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            await ReloadSchedulesAsync();

            StartNewSchedule();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(ScheduleDisplay? schedule)
    {
        if (schedule is null || IsBusy)
        {
            return;
        }

        bool confirmed = await Shell.Current.DisplayAlertAsync(
            AppResources.DeleteSchedule,
            string.Format(AppResources.DeleteScheduleConfirm, schedule.Title),
            AppResources.Delete,
            AppResources.Cancel);

        if (!confirmed)
        {
            return;
        }

        try
        {
            IsBusy = true;

            Result result = await ScopedHandler.HandleAsync((DeleteSchedule h) =>
                h.Handle(new DeleteSchedule.Request(schedule.Id)));

            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            await ReloadSchedulesAsync();

            if (Editing?.Id == schedule.Id)
            {
                StartNewSchedule();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void Close()
    {
        WeakReferenceMessenger.Default.Send(new SchedulesMessage(false));
    }

    private async Task ToggleEnabledAsync(ScheduleDisplay schedule)
    {
        bool wanted = schedule.IsEnabled;

        var request = new UpdateSchedule.Request(
            schedule.Id,
            schedule.DriveGroupId,
            schedule.Action,
            schedule.TimeOfDay,
            schedule.Days,
            wanted);

        Result result = await ScopedHandler.HandleAsync((UpdateSchedule h) => h.Handle(request));
        if (result.IsFailure)
        {
            schedule.SetEnabledQuietly(!wanted);

            await DisplayErrorAsync(result.Error);
        }
    }

    private async Task ReloadSchedulesAsync()
    {
        Result<List<Schedule>> result = await ScopedHandler.HandleAsync((GetSchedules h) => h.Handle());
        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        Schedules = [.. result.Value.Select(schedule => new ScheduleDisplay(
            schedule,
            schedule.DriveGroupId is Guid id ? _groupNames.GetValueOrDefault(id) : null,
            ToggleEnabledAsync))];
    }

    private void SetDays(ScheduleDays days)
    {
        Days = [.. ScheduleText.WeekInCultureOrder().Select(day => new ScheduleDayOption(day, days.Includes(day)))];
    }

    private ScheduleDays SelectedDays() => Days
        .Where(day => day.IsSelected)
        .Aggregate(ScheduleDays.None, (days, day) => days | day.Day.ToScheduleDay());

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<SchedulesMessage>(this, (r, m) =>
        {
            if (m.Show)
            {
                _ = LoadAsync();
            }
        });
    }
}

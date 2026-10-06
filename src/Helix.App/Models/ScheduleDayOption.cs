using CommunityToolkit.Mvvm.ComponentModel;

namespace Helix.App.Models;

internal sealed partial class ScheduleDayOption : ObservableObject
{
    public ScheduleDayOption(DayOfWeek day, bool isSelected)
    {
        Day = day;
        Label = ScheduleText.ShortName(day);
        IsSelected = isSelected;
    }

    public DayOfWeek Day { get; }

    public string Label { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Settings;

namespace Helix.App.Models;

internal sealed partial class SettingsDisplay : ObservableObject
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private Guid _userId;

    [ObservableProperty]
    private bool _autoConnect;

    [ObservableProperty]
    private bool _autoMinimize;

    [ObservableProperty]
    private bool _setOnStartup;

    [ObservableProperty]
    private int _timerCount;

    [ObservableProperty]
    private Language _language;

    public SettingsDisplay(Settings settings)
    {
        Id = settings.Id;
        UserId = settings.UserId;
        AutoConnect = settings.AutoConnect;
        AutoMinimize = settings.AutoMinimize;
        SetOnStartup = settings.SetOnStartup;
        TimerCount = settings.TimerCount;
        Language = settings.Language;
    }
}

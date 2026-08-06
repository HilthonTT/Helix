using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Application.Settings;
using Helix.Domain.Settings;
using SharedKernel;

namespace Helix.App.Models;

internal sealed partial class SettingsDisplay : ObservableObject
{
    private readonly System.Timers.Timer _debounceTimer;

    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private Guid _userId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private bool _autoConnect;
    async partial void OnAutoConnectChanged(bool value)
    {
        await UpdatePropertyAsync(builder => builder.AutoConnect = value);
    }

    [ObservableProperty]
    private bool _autoMinimize;
    async partial void OnAutoMinimizeChanged(bool value)
    {
        await UpdatePropertyAsync(builder => builder.AutoMinimize = value);
    }

    [ObservableProperty]
    private bool _setOnStartup;
    async partial void OnSetOnStartupChanged(bool value)
    {
        await UpdatePropertyAsync(builder => builder.SetOnStartup = value);
    }

    [ObservableProperty]
    private bool _setDesktopShortcut;
    async partial void OnSetDesktopShortcutChanged(bool value)
    {
        await UpdatePropertyAsync(builder => builder.SetDesktopShortcut = value);
    }

    [ObservableProperty]
    private int _timerCount;
    partial void OnTimerCountChanged(int value)
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    [ObservableProperty]
    private Language _language;
    async partial void OnLanguageChanged(Language value)
    {
        await UpdatePropertyAsync(builder => builder.Language = value);
    }

    public SettingsDisplay(Settings settings)
    {
        // Assign the backing fields, not the generated properties: property setters
        // fire the On…Changed hooks, which would issue UpdateSettings writes while the
        // remaining fields still hold half-initialized values (e.g. TimerCount = 0).
        _id = settings.Id;
        _userId = settings.UserId;
        _autoConnect = settings.AutoConnect;
        _autoMinimize = settings.AutoMinimize;
        _setOnStartup = settings.SetOnStartup;
        _setDesktopShortcut = settings.SetDesktopShortcut;
        _timerCount = settings.TimerCount;
        _language = settings.Language;

        _debounceTimer = new(500)
        {
            AutoReset = false
        };

        // Elapsed fires on a thread-pool thread; the update mutates bound properties
        // (IsBusy/IsNotBusy), so marshal the whole thing onto the UI thread.
        _debounceTimer.Elapsed += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await DebouncedUpdateTimerCount());
    }

    private async Task UpdatePropertyAsync(Action<UpdateSettings.Request.Builder> updateAction)
    {
        try
        {
            IsBusy = true;

            var requestBuilder = new UpdateSettings.Request.Builder(
                AutoConnect,
                AutoMinimize,
                SetOnStartup,
                SetDesktopShortcut,
                TimerCount,
                Language);

            // Apply the specific update.
            updateAction(requestBuilder);

            UpdateSettings.Request request = requestBuilder.Build();

            Result result = await ScopedHandler.HandleAsync((UpdateSettings h) => h.Handle(request));
            if (result.IsFailure)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Shell.Current.DisplayAlertAsync("Something went wrong!", result.Error.Description, "Ok");
                });
            }
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.DisplayAlertAsync("Something went wrong!", ex.Message, "Ok");
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DebouncedUpdateTimerCount()
    {
        await UpdatePropertyAsync(builder => builder.TimerCount = TimerCount);
    }
}

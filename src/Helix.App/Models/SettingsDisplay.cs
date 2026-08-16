using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Domain.Settings;

namespace Helix.App.Models;

internal sealed partial class SettingsDisplay : ObservableObject
{
    private readonly System.Timers.Timer _debounceTimer;

    // Stays false for the duration of the constructor. The On…Changed hooks issue
    // UpdateSettings writes, which must not run while the constructor is still seeding
    // properties and the rest still hold half-initialized values (e.g. TimerCount = 0).
    private readonly bool _initialized;

    [ObservableProperty]
    public partial Guid Id { get; set; }

    [ObservableProperty]
    public partial Guid UserId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    public partial bool AutoConnect { get; set; }
    async partial void OnAutoConnectChanged(bool value)
    {
        if (!_initialized)
        {
            return;
        }

        await UpdatePropertyAsync(builder => builder.AutoConnect = value);
    }

    [ObservableProperty]
    public partial bool AutoMinimize { get; set; }
    async partial void OnAutoMinimizeChanged(bool value)
    {
        if (!_initialized)
        {
            return;
        }

        await UpdatePropertyAsync(builder => builder.AutoMinimize = value);
    }

    [ObservableProperty]
    public partial bool SetOnStartup { get; set; }
    async partial void OnSetOnStartupChanged(bool value)
    {
        if (!_initialized)
        {
            return;
        }

        await UpdatePropertyAsync(builder => builder.SetOnStartup = value);
    }

    [ObservableProperty]
    public partial bool SetDesktopShortcut { get; set; }
    async partial void OnSetDesktopShortcutChanged(bool value)
    {
        if (!_initialized)
        {
            return;
        }

        await UpdatePropertyAsync(builder => builder.SetDesktopShortcut = value);
    }

    [ObservableProperty]
    public partial int TimerCount { get; set; }
    partial void OnTimerCountChanged(int value)
    {
        if (!_initialized)
        {
            return;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    [ObservableProperty]
    public partial Language Language { get; set; }
    async partial void OnLanguageChanged(Language value)
    {
        if (!_initialized)
        {
            return;
        }

        await UpdatePropertyAsync(builder => builder.Language = value);
    }

    public SettingsDisplay(Settings settings)
    {
        _debounceTimer = new(500)
        {
            AutoReset = false
        };

        // Elapsed fires on a thread-pool thread; the update mutates bound properties
        // (IsBusy/IsNotBusy), so marshal the whole thing onto the UI thread.
        _debounceTimer.Elapsed += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await DebouncedUpdateTimerCount());

        Id = settings.Id;
        UserId = settings.UserId;
        AutoConnect = settings.AutoConnect;
        AutoMinimize = settings.AutoMinimize;
        SetOnStartup = settings.SetOnStartup;
        SetDesktopShortcut = settings.SetDesktopShortcut;
        TimerCount = settings.TimerCount;
        Language = settings.Language;

        // Every seed above is done — from here on the hooks may write back.
        _initialized = true;
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

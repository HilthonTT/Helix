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

    // Set while a rejected value is being rolled back, so the write-back hook that the
    // rollback itself fires does not issue a second UpdateSettings call.
    private bool _rollingBack;

    // Last value the store accepted, so a rejected TimerCount can be put back.
    private int _persistedTimerCount;

    // Same again for the retention box, which is typed into the same way.
    private readonly System.Timers.Timer _retentionDebounceTimer;
    private int _persistedRetentionDays;

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
        if (!_initialized || _rollingBack)
        {
            return;
        }

        if (!await UpdatePropertyAsync(builder => builder.AutoConnect = value))
        {
            RollBack(() => AutoConnect = !value);
        }
    }

    [ObservableProperty]
    public partial bool AutoMinimize { get; set; }
    async partial void OnAutoMinimizeChanged(bool value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        if (!await UpdatePropertyAsync(builder => builder.AutoMinimize = value))
        {
            RollBack(() => AutoMinimize = !value);
        }
    }

    [ObservableProperty]
    public partial bool SetOnStartup { get; set; }
    async partial void OnSetOnStartupChanged(bool value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        if (!await UpdatePropertyAsync(builder => builder.SetOnStartup = value))
        {
            RollBack(() => SetOnStartup = !value);
        }
    }

    [ObservableProperty]
    public partial bool SetDesktopShortcut { get; set; }
    async partial void OnSetDesktopShortcutChanged(bool value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        if (!await UpdatePropertyAsync(builder => builder.SetDesktopShortcut = value))
        {
            RollBack(() => SetDesktopShortcut = !value);
        }
    }

    [ObservableProperty]
    public partial int TimerCount { get; set; }
    partial void OnTimerCountChanged(int value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    /// <summary>
    /// Days of audit history to keep; 0 keeps everything. Debounced like
    /// <see cref="TimerCount"/> — it is typed into digit by digit, and "9" on the way to
    /// "90" must not be saved and acted on.
    /// </summary>
    [ObservableProperty]
    public partial int AuditlogRetentionDays { get; set; }
    partial void OnAuditlogRetentionDaysChanged(int value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        _retentionDebounceTimer.Stop();
        _retentionDebounceTimer.Start();
    }

    [ObservableProperty]
    public partial Language Language { get; set; }
    async partial void OnLanguageChanged(Language value)
    {
        if (!_initialized || _rollingBack)
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

        _retentionDebounceTimer = new(500)
        {
            AutoReset = false
        };

        _retentionDebounceTimer.Elapsed += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await DebouncedUpdateRetentionDays());

        Id = settings.Id;
        UserId = settings.UserId;
        AutoConnect = settings.AutoConnect;
        AutoMinimize = settings.AutoMinimize;
        SetOnStartup = settings.SetOnStartup;
        SetDesktopShortcut = settings.SetDesktopShortcut;
        TimerCount = settings.TimerCount;
        _persistedTimerCount = settings.TimerCount;
        Language = settings.Language;
        AuditlogRetentionDays = settings.AuditlogRetentionDays;
        _persistedRetentionDays = settings.AuditlogRetentionDays;

        // Every seed above is done — from here on the hooks may write back.
        _initialized = true;
    }

    /// <summary>
    /// Pushes the current state through UpdateSettings. Returns false when the store
    /// rejected it, so the caller can put the control back where it was — the alert
    /// alone used to leave a switch showing a setting that had never been saved.
    /// </summary>
    private async Task<bool> UpdatePropertyAsync(Action<UpdateSettings.Request.Builder> updateAction)
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
                Language,
                AuditlogRetentionDays);

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

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.DisplayAlertAsync("Something went wrong!", ex.Message, "Ok");
            });

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Restores a rejected value without the restore itself being written back.
    /// </summary>
    private void RollBack(Action restore)
    {
        _rollingBack = true;

        try
        {
            restore();
        }
        finally
        {
            _rollingBack = false;
        }
    }

    private async Task DebouncedUpdateTimerCount()
    {
        int attempted = TimerCount;

        if (await UpdatePropertyAsync(builder => builder.TimerCount = attempted))
        {
            _persistedTimerCount = attempted;
            return;
        }

        RollBack(() => TimerCount = _persistedTimerCount);
    }

    private async Task DebouncedUpdateRetentionDays()
    {
        int attempted = AuditlogRetentionDays;

        if (await UpdatePropertyAsync(builder => builder.AuditlogRetentionDays = attempted))
        {
            _persistedRetentionDays = attempted;
            return;
        }

        RollBack(() => AuditlogRetentionDays = _persistedRetentionDays);
    }
}

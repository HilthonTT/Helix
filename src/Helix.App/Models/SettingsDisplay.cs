using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Settings;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Domain.Settings;
using Helix.App.Resources.Languages;
using Helix.App.Services;

namespace Helix.App.Models;

internal sealed partial class SettingsDisplay : ObservableObject
{
    private readonly System.Timers.Timer _debounceTimer;

    private readonly bool _initialized;

    private bool _rollingBack;

    private int _persistedTimerCount;

    private readonly System.Timers.Timer _retentionDebounceTimer;
    private int _persistedRetentionDays;

    private readonly System.Timers.Timer _storageAlertDebounceTimer;
    private int _persistedStorageAlertThresholdPercent;

    private readonly System.Timers.Timer _idleLockDebounceTimer;
    private int _persistedIdleLockMinutes;
    private Language _persistedLanguage;

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
    public partial int StorageAlertThresholdPercent { get; set; }
    partial void OnStorageAlertThresholdPercentChanged(int value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        _storageAlertDebounceTimer.Stop();
        _storageAlertDebounceTimer.Start();
    }

    [ObservableProperty]
    public partial int IdleLockMinutes { get; set; }
    partial void OnIdleLockMinutesChanged(int value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        _idleLockDebounceTimer.Stop();
        _idleLockDebounceTimer.Start();
    }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }
    async partial void OnCloseToTrayChanged(bool value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        if (!await UpdatePropertyAsync(builder => builder.CloseToTray = value))
        {
            RollBack(() => CloseToTray = !value);
        }
    }

    [ObservableProperty]
    public partial bool NotifyOnMinimizeToTray { get; set; }
    async partial void OnNotifyOnMinimizeToTrayChanged(bool value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        if (!await UpdatePropertyAsync(builder => builder.NotifyOnMinimizeToTray = value))
        {
            RollBack(() => NotifyOnMinimizeToTray = !value);
        }
    }

    [ObservableProperty]
    public partial Language Language { get; set; }
    async partial void OnLanguageChanged(Language value)
    {
        if (!_initialized || _rollingBack)
        {
            return;
        }

        if (await UpdatePropertyAsync(builder => builder.Language = value))
        {
            _persistedLanguage = value;
            return;
        }

        RollBack(() => Language = _persistedLanguage);
        CultureSwitcher.SwitchCulture(_persistedLanguage);
    }

    public SettingsDisplay(Settings settings)
    {
        _debounceTimer = new(500)
        {
            AutoReset = false
        };

        _debounceTimer.Elapsed += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await DebouncedUpdateTimerCount());

        _retentionDebounceTimer = new(500)
        {
            AutoReset = false
        };

        _retentionDebounceTimer.Elapsed += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await DebouncedUpdateRetentionDays());

        _storageAlertDebounceTimer = new(500)
        {
            AutoReset = false
        };

        _storageAlertDebounceTimer.Elapsed += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await DebouncedUpdateStorageAlertThreshold());

        _idleLockDebounceTimer = new(500)
        {
            AutoReset = false
        };

        _idleLockDebounceTimer.Elapsed += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await DebouncedUpdateIdleLockMinutes());

        Id = settings.Id;
        UserId = settings.UserId;
        AutoConnect = settings.AutoConnect;
        AutoMinimize = settings.AutoMinimize;
        SetOnStartup = settings.SetOnStartup;
        SetDesktopShortcut = settings.SetDesktopShortcut;
        TimerCount = settings.TimerCount;
        _persistedTimerCount = settings.TimerCount;
        Language = settings.Language;
        _persistedLanguage = settings.Language;
        AuditlogRetentionDays = settings.AuditlogRetentionDays;
        _persistedRetentionDays = settings.AuditlogRetentionDays;
        StorageAlertThresholdPercent = settings.StorageAlertThresholdPercent;
        _persistedStorageAlertThresholdPercent = settings.StorageAlertThresholdPercent;
        IdleLockMinutes = settings.IdleLockMinutes;
        _persistedIdleLockMinutes = settings.IdleLockMinutes;
        CloseToTray = settings.CloseToTray;
        NotifyOnMinimizeToTray = settings.NotifyOnMinimizeToTray;

        _initialized = true;
    }

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
                AuditlogRetentionDays,
                StorageAlertThresholdPercent,
                IdleLockMinutes,
                CloseToTray,
                NotifyOnMinimizeToTray);

            updateAction(requestBuilder);

            UpdateSettings.Request request = requestBuilder.Build();

            Result result = await ScopedHandler.HandleAsync((UpdateSettings h) => h.Handle(request));
            if (result.IsFailure)
            {
                Notifier.Error(result.Error);

                return false;
            }

            WeakReferenceMessenger.Default.Send(new SettingsChangedMessage());

            return true;
        }
        catch (Exception ex)
        {
            Notifier.Error(ex.Message);

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

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

    private static void ConfirmSaved() => Notifier.Success(AppResources.SettingsSaved);

    private async Task DebouncedUpdateTimerCount()
    {
        int attempted = TimerCount;

        if (await UpdatePropertyAsync(builder => builder.TimerCount = attempted))
        {
            _persistedTimerCount = attempted;
            ConfirmSaved();
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
            ConfirmSaved();
            return;
        }

        RollBack(() => AuditlogRetentionDays = _persistedRetentionDays);
    }

    private async Task DebouncedUpdateStorageAlertThreshold()
    {
        int attempted = StorageAlertThresholdPercent;

        if (await UpdatePropertyAsync(builder => builder.StorageAlertThresholdPercent = attempted))
        {
            _persistedStorageAlertThresholdPercent = attempted;
            ConfirmSaved();
            return;
        }

        RollBack(() => StorageAlertThresholdPercent = _persistedStorageAlertThresholdPercent);
    }

    private async Task DebouncedUpdateIdleLockMinutes()
    {
        int attempted = IdleLockMinutes;

        if (await UpdatePropertyAsync(builder => builder.IdleLockMinutes = attempted))
        {
            _persistedIdleLockMinutes = attempted;
            ConfirmSaved();
            return;
        }

        RollBack(() => IdleLockMinutes = _persistedIdleLockMinutes);
    }
}

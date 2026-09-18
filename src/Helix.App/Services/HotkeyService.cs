using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Messaging.Settings;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Settings.Queries;
using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.Services;

internal sealed class HotkeyService
{
    private readonly IGlobalHook _hook;
    private readonly IDriveMonitor _monitor;
    private readonly TrayIconService _tray;
    private readonly ILogger<HotkeyService> _logger;

    private volatile bool _enabled = SettingsModel.DefaultGlobalHotkeys;
    private bool _running;
    private bool _subscribed;
    private int _busy;

    public HotkeyService(
        IGlobalHook hook,
        IDriveMonitor monitor,
        TrayIconService tray,
        ILogger<HotkeyService> logger)
    {
        _hook = hook;
        _monitor = monitor;
        _tray = tray;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        if (!_subscribed)
        {
            WeakReferenceMessenger.Default.Register<SettingsChangedMessage>(this, (r, m) => ReloadSafely());

            _subscribed = true;
        }

        if (!_running)
        {
            _hook.KeyPressed += OnKeyPressed;
            _running = true;
        }

        await LoadPreferenceAsync();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _hook.KeyPressed -= OnKeyPressed;
        _running = false;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (!_enabled || !HasChord(e.RawEvent.Mask))
        {
            return;
        }

        bool? disconnect = e.Data.KeyCode switch
        {
            KeyCode.VcC => false,
            KeyCode.VcD => true,
            _ => null,
        };

        if (disconnect is null || IsLocked())
        {
            return;
        }

        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        _ = RunAsync(disconnect.Value);
    }

    private static bool HasChord(EventMask mask) =>
        (mask & EventMask.Ctrl) != 0 &&
        (mask & EventMask.Alt) != 0 &&
        (mask & EventMask.Shift) != 0 &&
        (mask & EventMask.Meta) == 0;

    private static bool IsLocked() =>
        App.ServiceProvider.GetRequiredService<IdleLockService>().IsLocked;

    private async Task RunAsync(bool disconnect)
    {
        try
        {
            Result result = disconnect
                ? await ScopedHandler.HandleAsync((DisconnectAllDrives h) => h.Handle())
                : await ScopedHandler.HandleAsync((ConnectAllDrives h) => h.Handle());

            await _monitor.PollAsync();

            MainThread.BeginInvokeOnMainThread(() =>
                WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage()));

            await _tray.RefreshAsync();

            if (result.IsFailure)
            {
                _logger.LogWarning("A global shortcut failed: {Reason}", result.Error.Description);

                Tell(result.Error.Description, failed: true);

                return;
            }

            Tell(disconnect ? AppResources.HotkeyDisconnectedAll : AppResources.HotkeyConnectedAll, failed: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A global shortcut faulted.");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void Tell(string message, bool failed)
    {
        if (_tray.IsRunning)
        {
            _tray.Notify(AppInfo.Current.Name, message);
            return;
        }

        if (failed)
        {
            Notifier.Error(message);
        }
        else
        {
            Notifier.Success(message);
        }
    }

    private async Task LoadPreferenceAsync()
    {
        Result<SettingsModel> result = await ScopedHandler.HandleAsync((GetSettings h) => h.Handle());
        if (result.IsFailure)
        {
            return;
        }

        _enabled = result.Value.GlobalHotkeys;
    }

    private void ReloadSafely()
    {
        _ = LoadPreferenceAsync().ContinueWith(
            task => _logger.LogError(task.Exception, "The global shortcuts failed to re-read their setting."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

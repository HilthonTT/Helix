using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Messaging.Navigation;
using Helix.App.Models;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Domain.Drives;
using System.Collections.ObjectModel;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class HomeViewModel : BaseViewModel
{
    private readonly INasConnector _nasConnector;

    public HomeViewModel()
    {
        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();

        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Drives = [];
        TotalStorage = string.Empty;
        TotalConnected = string.Empty;

        RegisterMessages();
        InitializeCountdownEvents();
    }

    [ObservableProperty]
    public partial ObservableCollection<DriveDisplay> Drives { get; set; }

    [ObservableProperty]
    public partial string TotalStorage { get; set; }

    [ObservableProperty]
    public partial string TotalConnected { get; set; }

    /// <summary>Backs the connectivity legend beside the donut chart.</summary>
    [ObservableProperty]
    public partial int ConnectedCount { get; set; }

    [ObservableProperty]
    public partial int DisconnectedCount { get; set; }

    [RelayCommand]
    private static void OpenCreateDriveModal()
    {
        WeakReferenceMessenger.Default.Send(new CreateDriveMessage(true));
    }

    [RelayCommand]
    private static void OpenSearchDrivesModal()
    {
        WeakReferenceMessenger.Default.Send(new SearchDrivesMessage(true));
    }

    [RelayCommand]
    private async Task ExportDrivesAsync()
    {
        Result result = await ScopedHandler.HandleAsync((ExportDrives h) => h.Handle());
        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        await DisplaySuccessAsync("You've exported your drives!");
    }

    [RelayCommand]
    private async Task ImportDrivesAsync()
    {
        Result<List<Drive>> result = await ScopedHandler.HandleAsync((ImportDrives h) => h.Handle());
        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        await DisplaySuccessAsync("You've imported your drives!");

        List<Drive> drives = result.Value;

        if (drives.Count != 0)
        {
            foreach (Drive drive in drives)
            {
                Drives.Add(new DriveDisplay(drive));
            }

            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());
        }
    }

    [RelayCommand]
    private async static Task GoToSettingsAsync()
    {
        await Shell.Current.GoToAsync($"//{PageNames.SettingsPage}");

        WeakReferenceMessenger.Default.Send(new PageChangedMessage(PageNames.SettingsPage));
    }

    [RelayCommand]
    private async Task ConnectDrivesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            // ConnectAllDrives.Handle is already fully async — no need to offload via Task.Run.
            Result result = await ScopedHandler.HandleAsync((ConnectAllDrives h) => h.Handle());

            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

            foreach (DriveDisplay drive in Drives)
            {
                WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(drive.Id));
            }

            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectDrivesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            await ScopedHandler.HandleAsync((DisconnectAllDrives h) => h.Handle());

            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

            foreach (DriveDisplay drive in Drives)
            {
                WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(drive.Id));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConnectDrivesOnStartupAsync()
    {
        Result<SettingsModel> result = await ScopedHandler.HandleAsync((GetSettings h) => h.Handle());
        if (result.IsFailure)
        {
            return;
        }

        SettingsModel settings = result.Value;

        CultureSwitcher.SwitchCulture(settings.Language);

        if (!settings.AutoConnect)
        {
            return;
        }

        // Batch lookup — single DriveInfo.GetDrives() scan rather than per-drive.
        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();
        if (Drives.All(d => connectedLetters.Contains(d.Letter)))
        {
            return;
        }

        // ConnectAllDrives.Handle internally runs Task.WhenAll across all disconnected
        // drives — much faster than the previous serial per-drive message dispatch.
        Result connectResult = await ScopedHandler.HandleAsync((ConnectAllDrives h) => h.Handle());

        WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

        foreach (DriveDisplay drive in Drives)
        {
            WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(drive.Id));
        }

        if (connectResult.IsFailure)
        {
            await DisplayErrorAsync(connectResult.Error);
        }
    }

    public async Task<List<Drive>> FetchDrivesAsync()
    {
        Result<List<Drive>> result = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
        if (result.IsFailure)
        {
            return [];
        }

        List<Drive> drives = result.Value;

        Drives = new(drives.Select(d => new DriveDisplay(d)));

        await RefreshTotalsAsync();

        return drives;
    }

    /// <summary>
    /// Recomputes the dashboard tiles. The connection count is a cheap logical-drive
    /// lookup, but the capacity figure does I/O against the share, so it is probed off
    /// the UI thread — the monitor drives this on a timer now, and an unreachable NAS
    /// would otherwise stall the app on every poll.
    /// </summary>
    private async Task RefreshTotalsAsync()
    {
        try
        {
            TotalConnected = ValidateTotalConnected();
            TotalStorage = await ValidateTotalStorageAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Helix: failed to refresh dashboard totals: {ex}");
        }
    }

    private Task<string> ValidateTotalStorageAsync()
    {
        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();
        DriveDisplay? connectedDrive = Drives.FirstOrDefault(d => connectedLetters.Contains(d.Letter));

        if (connectedDrive is null)
        {
            return Task.FromResult("0 TB");
        }

        return StorageUsageHelper.GetCompactUsageAsync(connectedDrive.Letter);
    }

    private string ValidateTotalConnected()
    {
        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();
        int count = Drives.Count(d => connectedLetters.Contains(d.Letter));

        ConnectedCount = count;
        DisconnectedCount = Drives.Count - count;

        return $"{count} / {Drives.Count}";
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<CheckDrivesStatusMessage>(this, (r, m) =>
        {
            _ = RefreshTotalsAsync();
        });

        WeakReferenceMessenger.Default.Register<DriveDeletedMessage>(this, (r, m) =>
        {
            DriveDisplay? existingDrive = Drives.FirstOrDefault(d => d.Id == m.DriveId);
            if (existingDrive is not null)
            {
                Drives.Remove(existingDrive);

                _ = RefreshTotalsAsync();
            }
        });

        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(this, (r, m) =>
        {
            var driveDisplay = new DriveDisplay(m.Drive);
            Drives.Add(driveDisplay);

            _ = RefreshTotalsAsync();
        });

        WeakReferenceMessenger.Default.Register<DriveSearchedMessage>(this, (r, m) =>
        {
            IEnumerable<DriveDisplay> drives = m.SearchedDrives.Select(d => new DriveDisplay(d));

            Drives = new(drives);
        });
    }
}

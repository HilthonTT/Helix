using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Constants;
using Helix.App.Helpers;
using Helix.App.Messages;
using Helix.App.Modals.Drives.Create;
using Helix.App.Modals.Drives.Delete;
using Helix.App.Modals.Drives.Search;
using Helix.App.Models;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Drives;
using Helix.Application.Settings;
using Helix.Domain.Drives;
using SharedKernel;
using System.Collections.ObjectModel;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.Pages.Home;

internal sealed partial class HomeViewModel : BaseViewModel
{
    private readonly INasConnector _nasConnector;
    private readonly GetSettings _getSettings;
    private readonly GetDrives _getDrives;
    private readonly ExportDrives _exportDrives;
    private readonly ImportDrives _importDrives;
    private readonly ConnectAllDrives _connectAllDrives;
    private readonly DisconnectAllDrives _disconnectAllDrives;

    public HomeViewModel()
    {
        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _getSettings = App.ServiceProvider.GetRequiredService<GetSettings>();
        _getDrives = App.ServiceProvider.GetRequiredService<GetDrives>();
        _exportDrives = App.ServiceProvider.GetRequiredService<ExportDrives>();
        _importDrives = App.ServiceProvider.GetRequiredService<ImportDrives>();
        _connectAllDrives = App.ServiceProvider.GetRequiredService<ConnectAllDrives>();
        _disconnectAllDrives = App.ServiceProvider.GetRequiredService<DisconnectAllDrives>();

        RegisterMessages();
        InitializeCountdownEvents();
    }

    [ObservableProperty]
    private ObservableCollection<DriveDisplay> _drives = [];

    [ObservableProperty]
    private string _totalStorage = string.Empty;

    [ObservableProperty]
    private string _totalConnected = string.Empty;

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
        Result result = await _exportDrives.Handle();
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
        Result<List<Drive>> result = await _importDrives.Handle();
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

            bool isFailure = false;
            Error error = Error.None;

            // Run the drive connection process on a background thread
            await Task.Run(async () =>
            {
                Result result = await _connectAllDrives.Handle();
                isFailure = result.IsFailure;
                error = result.Error;

                WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());
            });

            foreach (DriveDisplay drive in Drives)
            {
                WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(drive.Id));
            }

            if (isFailure)
            {
                await DisplayErrorAsync(error);
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

            await _disconnectAllDrives.Handle();

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
        Result<SettingsModel> result = await _getSettings.Handle();
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

        List<DriveDisplay> disconnectedDrives = Drives
            .Where(d => !_nasConnector.IsConnected(d.Letter))
            .ToList();

        foreach (DriveDisplay disconnectedDrive in disconnectedDrives)
        {
            WeakReferenceMessenger.Default.Send(new ToggleConnectDriveMessage(disconnectedDrive.Id));
        }
    }

    public async Task<List<Drive>> FetchDrivesAsync()
    {
        Result<List<Drive>> result = await _getDrives.Handle();
        if (result.IsFailure)
        {
            return [];
        }

        List<Drive> drives = result.Value;

        Drives = new(drives.Select(d => new DriveDisplay(d)));

        TotalStorage = ValidateTotalStorage();
        TotalConnected = ValidateTotalConnected();

        return drives;
    }

    private string ValidateTotalStorage()
    {
        DriveDisplay? connectedDrive = Drives
            .Where(d => _nasConnector.IsConnected(d.Letter))
            .FirstOrDefault();

        if (connectedDrive is null)
        {
            return $"0 GB";
        }

        return StorageUsageHelper.GetStorageUsage(connectedDrive.Letter, "0 GB");
    }

    private string ValidateTotalConnected()
    {
        List<DriveDisplay> connectedDrives = Drives
            .Where(d => _nasConnector.IsConnected(d.Letter))
            .ToList();

        return $"{connectedDrives.Count} / {Drives.Count}";
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<CheckDrivesStatusMessage>(this, (r, m) =>
        {
            TotalStorage = ValidateTotalStorage();
            TotalConnected = ValidateTotalConnected();
        });

        WeakReferenceMessenger.Default.Register<DriveDeletedMessage>(this, (r, m) =>
        {
            DriveDisplay? existingDrive = Drives.FirstOrDefault(d => d.Id == m.DriveId);
            if (existingDrive is not null)
            {
                Drives.Remove(existingDrive);

                TotalStorage = ValidateTotalStorage();
                TotalConnected = ValidateTotalConnected();
            }
        });

        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(this, (r, m) =>
        {
            var driveDisplay = new DriveDisplay(m.Drive);
            Drives.Add(driveDisplay);

            TotalStorage = ValidateTotalStorage();
            TotalConnected = ValidateTotalConnected();
        });

        WeakReferenceMessenger.Default.Register<DriveSearchedMessage>(this, (r, m) =>
        {
            IEnumerable<DriveDisplay> drives = m.SearchedDrives.Select(d => new DriveDisplay(d));

            Drives = new(drives);
        });
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Constants;
using Helix.App.Helpers;
using Helix.App.Messages;
using Helix.App.Modals.Drives.Create;
using Helix.App.Modals.Drives.Delete;
using Helix.App.Models;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Time;
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
    private readonly ICountdownService _countdownService;

    private readonly GetDrives _getDrives;
    private readonly GetSettings _getSettings;
    private readonly ExportDrives _exportDrives;
    private readonly ImportDrives _importDrives;

    public HomeViewModel()
    {
        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _countdownService = App.ServiceProvider.GetRequiredService<ICountdownService>();

        _getDrives = App.ServiceProvider.GetRequiredService<GetDrives>();
        _exportDrives = App.ServiceProvider.GetRequiredService<ExportDrives>();
        _importDrives = App.ServiceProvider.GetRequiredService<ImportDrives>();
        _getSettings = App.ServiceProvider.GetRequiredService<GetSettings>();

        RegisterMessages();
        FetchDrives();

        InitializeCountdownEvents();
    }

    [ObservableProperty]
    private ObservableCollection<DriveDisplay> _drives = [];

    [ObservableProperty]
    private string _totalStorage = string.Empty;

    [ObservableProperty]
    private string _totalConnected = string.Empty;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimerCount))]
    private int _secondsRemaining = 0;

    public string TimerCount => $"{SecondsRemaining} seconds";

    [ObservableProperty]
    private bool _timerCancelled;

    [ObservableProperty]
    private bool _showRedoButton;

    [RelayCommand]
    private static void OpenCreateDriveModal()
    {
        WeakReferenceMessenger.Default.Send(new CreateDriveMessage(true));
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

        foreach (Drive drive in result.Value)
        {
            Drives.Add(new DriveDisplay(drive));
        }
    }

    [RelayCommand]
    private async static Task GoToSettingsAsync()
    {
        await Shell.Current.GoToAsync($"//{PageNames.SettingsPage}");

        WeakReferenceMessenger.Default.Send(new PageChangedMessage(PageNames.SettingsPage));
    }

    [RelayCommand]
    private void ResumeTimer()
    {
        _countdownService.Resume();
        TimerCancelled = false;
    }

    [RelayCommand]
    private void CancelTimer()
    {
        _countdownService.Stop();

        TimerCancelled = true;
    }

    [RelayCommand]
    private async Task StartTimerAsync()
    {
        Result<SettingsModel> result = await _getSettings.Handle();
        if (result.IsSuccess)
        {
            SettingsModel settings = result.Value;
            if (settings.AutoMinimize)
            {
                _countdownService.Start(settings.TimerCount);

                ShowRedoButton = false;
            }
        }
    }

    private void FetchDrives()
    {
        Result<List<Drive>> result = _getDrives.Handle().GetAwaiter().GetResult();
        if (result.IsFailure)
        {
            return;
        }

        Drives = new(result.Value.Select(d => new DriveDisplay(d)));

        TotalStorage = ValidateTotalStorage();
        TotalConnected = ValidateTotalConnected();
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
    }

    private void InitializeCountdownEvents()
    {
        Task.Run(async () =>
        {
            Result<SettingsModel> result = await _getSettings.Handle();
            if (result.IsSuccess)
            {
                SettingsModel settings = result.Value;
                if (settings.AutoMinimize)
                {
                    _countdownService.Start(settings.TimerCount);
                }
            }
        });

        _countdownService.CountdownTick += (sender, remaining) =>
        {
            // Update the view model property for binding
            SecondsRemaining = remaining; 
        };

        _countdownService.CountdownFinished += async (sender, args) =>
        {
            // Perform action when timer reaches 0
            ShowRedoButton = true;
            TimerCancelled = true;
            MinimizeApp();
        };
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.DriveGroups;
using Helix.App.Messaging.Drives;
using Helix.App.Messaging.Navigation;
using Helix.App.Messaging.Schedules;
using Helix.App.Messaging.Storage;
using Helix.App.Icons;
using Helix.App.Models;
using Helix.Application.Core.Sorting;
using Microsoft.Extensions.Logging;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Storage;
using Helix.Application.Features.DriveGroups.Queries;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Contracts;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Application.Features.Storage.Contracts;
using Helix.Application.Features.Storage.Queries;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using System.Collections.ObjectModel;
using SettingsModel = Helix.Domain.Settings.Settings;
using Helix.App.Resources.Languages;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class HomeViewModel : BaseViewModel
{
    private readonly INasConnector _nasConnector;
    private readonly IStorageProbe _storageProbe;

    public HomeViewModel()
    {
        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _storageProbe = App.ServiceProvider.GetRequiredService<IStorageProbe>();

        Drives = [];
        DriveGroups = [];
        SearchTerm = string.Empty;
        SortField = DriveSortField.Letter;
        SortOrder = SortOrder.Ascending;
        TotalStorage = string.Empty;
        TotalConnected = string.Empty;
        StorageForecastText = string.Empty;
        StorageForecastDetail = string.Empty;

        RegisterMessages();
        InitializeCountdownEvents();

        LocalizationResourceManager.Instance.PropertyChanged += (_, _) => DescribeStorageForecasts();
    }

    private void WatchSelection(ObservableCollection<DriveDisplay> drives)
    {
        drives.CollectionChanged += (_, e) =>
        {
            foreach (DriveDisplay added in e.NewItems?.OfType<DriveDisplay>() ?? [])
            {
                added.PropertyChanged += OnDrivePropertyChanged;
            }

            foreach (DriveDisplay removed in e.OldItems?.OfType<DriveDisplay>() ?? [])
            {
                removed.PropertyChanged -= OnDrivePropertyChanged;
            }

            RefreshSelection();
        };

        foreach (DriveDisplay drive in drives)
        {
            drive.PropertyChanged += OnDrivePropertyChanged;
        }

        RefreshSelection();
    }

    partial void OnDrivesChanged(ObservableCollection<DriveDisplay> oldValue, ObservableCollection<DriveDisplay> newValue)
    {
        foreach (DriveDisplay drive in oldValue ?? [])
        {
            drive.PropertyChanged -= OnDrivePropertyChanged;
        }

        WatchSelection(newValue);
    }

    private void OnDrivePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DriveDisplay.IsSelected))
        {
            RefreshSelection();
        }
    }

    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(SelectedDrives));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private readonly List<DriveDisplay> _allDrives = [];

    [ObservableProperty]
    public partial ObservableCollection<DriveDisplay> Drives { get; set; }

    public IReadOnlyList<DriveDisplay> SelectedDrives => [.. Drives.Where(drive => drive.IsSelected)];

    public bool HasSelection => Drives.Any(drive => drive.IsSelected);

    public bool AllSelected => Drives.Count > 0 && Drives.All(drive => drive.IsSelected);

    public string SelectionSummary => string.Format(AppResources.DrivesSelected, SelectedDrives.Count);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChipLabels))]
    public partial bool IsCompact { get; set; }

    partial void OnIsCompactChanged(bool value) => DriveListLayout.Instance.IsCompact = value;

    public bool ShowChipLabels => !IsCompact;

    [ObservableProperty]
    public partial ObservableCollection<DriveGroupDisplay> DriveGroups { get; set; }

    [ObservableProperty]
    public partial string TotalStorage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStorageForecast))]
    public partial string StorageForecastText { get; set; }

    [ObservableProperty]
    public partial string StorageForecastDetail { get; set; }

    public bool HasStorageForecast => StorageForecastText.Length != 0;

    private IReadOnlyList<StorageForecast> _storageForecasts = [];

    [ObservableProperty]
    public partial string TotalConnected { get; set; }

    [ObservableProperty]
    public partial int ConnectedCount { get; set; }

    [ObservableProperty]
    public partial int DisconnectedCount { get; set; }

    [RelayCommand]
    private static void OpenCreateDriveModal()
    {
        WeakReferenceMessenger.Default.Send(new CreateDriveMessage(true));
    }

    public bool SupportsAdoptingMappings => DrivePlatform.SupportsAdoptingMappings;

    [RelayCommand]
    private static void OpenAdoptMappingsModal()
    {
        WeakReferenceMessenger.Default.Send(new AdoptMappingsMessage(true));
    }

    [RelayCommand]
    private static void OpenDriveGroupsModal()
    {
        WeakReferenceMessenger.Default.Send(new DriveGroupsMessage(true));
    }

    [RelayCommand]
    private static void OpenSchedulesModal()
    {
        WeakReferenceMessenger.Default.Send(new SchedulesMessage(true));
    }

    [ObservableProperty]
    public partial string SearchTerm { get; set; }

    partial void OnSearchTermChanged(string value) => ApplyFilterAndSort();

    [ObservableProperty]
    public partial DriveSortField SortField { get; set; }

    [ObservableProperty]
    public partial SortOrder SortOrder { get; set; }

    public bool HasSearchTerm => !string.IsNullOrEmpty(SearchTerm);

    public bool ShowNoDrives => Drives.Count == 0 && !HasSearchTerm;

    public bool ShowNoMatches => Drives.Count == 0 && HasSearchTerm;

    [RelayCommand]
    private void SortBy(DriveSortField field)
    {
        if (SortField == field)
        {
            SortOrder = SortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
        }
        else
        {
            SortField = field;
            SortOrder = SortOrder.Ascending;
        }

        RefreshSortIndicators();
        ApplyFilterAndSort();
    }

    [RelayCommand]
    private void ClearSearch() => SearchTerm = string.Empty;

    private void ApplyFilterAndSort()
    {
        string term = SearchTerm.Trim();

        IEnumerable<DriveDisplay> matching = string.IsNullOrEmpty(term)
            ? _allDrives
            : _allDrives.Where(drive => Matches(drive, term));

        DriveDisplay[] visible = [.. Sort(matching)];

        foreach (DriveDisplay drive in _allDrives.Except(visible))
        {
            drive.IsSelected = false;
        }

        Reconcile(Drives, visible);

        OnPropertyChanged(nameof(HasSearchTerm));
        OnPropertyChanged(nameof(ShowNoDrives));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    private static void Reconcile(ObservableCollection<DriveDisplay> shown, DriveDisplay[] wanted)
    {
        var keep = new HashSet<DriveDisplay>(wanted);

        for (int i = shown.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(shown[i]))
            {
                shown.RemoveAt(i);
            }
        }

        for (int i = 0; i < wanted.Length; i++)
        {
            DriveDisplay drive = wanted[i];

            int at = shown.IndexOf(drive);
            if (at == i)
            {
                continue;
            }

            if (at >= 0)
            {
                shown.RemoveAt(at);
            }

            shown.Insert(i, drive);
        }
    }

    private static bool Matches(DriveDisplay drive, string term) =>
        drive.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        drive.Letter.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        drive.Host.Contains(term, StringComparison.OrdinalIgnoreCase);

    private IOrderedEnumerable<DriveDisplay> Sort(IEnumerable<DriveDisplay> drives)
    {
        bool descending = SortOrder == SortOrder.Descending;

        return SortField switch
        {
            DriveSortField.Name => descending
                ? drives.OrderByDescending(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
                : drives.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(d => d.Letter, StringComparer.OrdinalIgnoreCase),

            DriveSortField.Status => descending
                ? drives.OrderByDescending(d => d.Connected)
                    .ThenBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
                : drives.OrderBy(d => d.Connected)
                    .ThenBy(d => d.Letter, StringComparer.OrdinalIgnoreCase),

            _ => descending
                ? drives.OrderByDescending(d => d.Letter, StringComparer.OrdinalIgnoreCase)
                : drives.OrderBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void RefreshSortIndicators()
    {
        OnPropertyChanged(nameof(SortedByLetter));
        OnPropertyChanged(nameof(SortedByName));
        OnPropertyChanged(nameof(SortedByStatus));
        OnPropertyChanged(nameof(SortGlyph));
    }

    public bool SortedByLetter => SortField == DriveSortField.Letter;

    public bool SortedByName => SortField == DriveSortField.Name;

    public bool SortedByStatus => SortField == DriveSortField.Status;

    public string SortGlyph => SortOrder == SortOrder.Ascending ? IconFont.CaretUp : IconFont.CaretDown;

    [RelayCommand]
    private async Task ExportDrivesAsync()
    {
        Result result = await ScopedHandler.HandleAsync((ExportDrives h) => h.Handle());
        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        await DisplaySuccessAsync(AppResources.DrivesExported);
    }

    [RelayCommand]
    private async Task ImportDrivesAsync()
    {
        Result<VaultImport> result = await ScopedHandler.HandleAsync((ImportDrives h) => h.Handle());
        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        await DisplaySuccessAsync(AppResources.DrivesImported);

        if (result.Value.GroupCount != 0)
        {
            WeakReferenceMessenger.Default.Send(new DriveGroupsChangedMessage());
        }

        List<Drive> drives = result.Value.Drives;

        if (drives.Count != 0)
        {
            foreach (Drive drive in drives)
            {
                WeakReferenceMessenger.Default.Send(new DriveCreatedMessage(drive));
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
    private void ToggleSelectAll()
    {
        bool select = !AllSelected;

        foreach (DriveDisplay drive in Drives)
        {
            drive.IsSelected = select;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (DriveDisplay drive in Drives)
        {
            drive.IsSelected = false;
        }
    }

    [RelayCommand]
    private Task ConnectSelectedAsync() => RunOnSelectionAsync(disconnect: false);

    [RelayCommand]
    private Task DisconnectSelectedAsync() => RunOnSelectionAsync(disconnect: true);

    private async Task RunOnSelectionAsync(bool disconnect)
    {
        if (IsBusy)
        {
            return;
        }

        IReadOnlyList<DriveDisplay> selected = SelectedDrives;
        if (selected.Count == 0)
        {
            return;
        }

        if (disconnect && !await ConfirmDisconnectAsync([.. selected.Where(drive => drive.Connected).Select(drive => drive.Letter)]))
        {
            return;
        }

        Guid[] ids = [.. selected.Select(drive => drive.Id)];

        try
        {
            IsBusy = true;

            Result result = await ScopedHandler.HandleAsync(
                (ConnectDrives h) => h.Handle(new ConnectDrives.Request(ids, disconnect)));

            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

            foreach (Guid id in ids)
            {
                WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(id));
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

    private static Task<bool> ConfirmDisconnectAsync(IReadOnlyCollection<string> mountedLetters) =>
        DisconnectConfirmation.ConfirmAsync(mountedLetters);

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

        // Only what "disconnect all" will actually take down, which is the letters mapped from
        // each drive's own share (DriveMountBatch.IsMapped), not whatever happens to be on them.
        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();
        DriveDisplay[] rows = [.. _allDrives];

        string[] mapped = await Task.Run(() => rows
            .Where(drive => drive.IsMapped(_nasConnector, connectedLetters))
            .Select(drive => drive.Letter)
            .ToArray());

        if (!await ConfirmDisconnectAsync(mapped))
        {
            return;
        }

        try
        {
            IsBusy = true;

            Result result = await ScopedHandler.HandleAsync((DisconnectAllDrives h) => h.Handle());

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

        Result connectResult = await ScopedHandler.HandleAsync(
            (ConnectAllDrives h) => h.Handle(new ConnectAllDrives.Request(OnlyAutoConnect: true)));

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

        Dictionary<Guid, DriveDisplay> existing = [];

        foreach (DriveDisplay shown in _allDrives)
        {
            existing.TryAdd(shown.Id, shown);
        }

        _allDrives.Clear();

        foreach (Drive drive in drives)
        {
            if (existing.TryGetValue(drive.Id, out DriveDisplay? row))
            {
                row.Letter = drive.Letter;
                row.Name = drive.Name;
                row.Host = drive.Host;
                row.RemoteHost = drive.RemoteHost;
                row.Protocol = drive.Protocol;
                row.Port = drive.Port;
                row.LastConnectedOnUtc = drive.LastConnectedOnUtc;
                row.CanWake = drive.MacAddress is not null;
                row.RefreshLocalizedText();

                _allDrives.Add(row);
            }
            else
            {
                _allDrives.Add(new DriveDisplay(drive));
            }
        }

        SyncConnectivity(_nasConnector.GetLiveLetters());

        ApplyFilterAndSort();

        await RefreshTotalsAsync();

        return drives;
    }

    private void SyncConnectivity(HashSet<string> liveLetters)
    {
        foreach (DriveDisplay drive in _allDrives)
        {
            drive.Connected = drive.IsUp(_nasConnector, liveLetters);
        }
    }

    public async Task FetchDriveGroupsAsync()
    {
        Result<List<DriveGroup>> result = await ScopedHandler.HandleAsync((GetDriveGroups h) => h.Handle());
        if (result.IsFailure)
        {
            DriveGroups = [];

            return;
        }

        HashSet<Guid> existing = [.. _allDrives.Select(drive => drive.Id)];

        DriveGroups = [.. result.Value.Select(group => new DriveGroupDisplay(group, existing))];
    }

    public async Task FetchStorageForecastsAsync()
    {
        try
        {
            Result<List<StorageForecast>> result = await Task.Run(() =>
                ScopedHandler.HandleAsync((GetStorageForecasts h) => h.Handle()));

            _storageForecasts = result.IsSuccess ? result.Value : [];

            DescribeStorageForecasts();
        }
        catch (Exception ex)
        {
            AppLog.For<HomeViewModel>().LogError(ex, "Failed to read the storage forecast.");
        }
    }

    private void DescribeStorageForecasts()
    {
        StorageForecast? soonest = _storageForecasts.FirstOrDefault(forecast =>
            forecast.DaysUntilFull <= StorageForecastHorizonDays);

        if (soonest is not { DaysUntilFull: int days, BytesPerDay: double bytesPerDay })
        {
            StorageForecastText = string.Empty;
            StorageForecastDetail = string.Empty;

            return;
        }

        StorageForecastText = days switch
        {
            < 1 => AppResources.StorageFullWithinADay,
            < 60 => string.Format(AppResources.StorageFullInDays, days),
            _ => string.Format(AppResources.StorageFullInMonths, (int)Math.Round(days / 30.0)),
        };

        StorageForecastDetail = string.Format(
            AppResources.StorageForecastDetail,
            string.Join(", ", soonest.DriveNames),
            (-bytesPerDay / 1_000_000_000).ToString("F1"),
            StorageUsageHelper.FormatCombined(soonest.TotalBytes - soonest.FreeBytes, soonest.TotalBytes));
    }

    private const int StorageForecastHorizonDays = 365;

    private int _totalsRequest;

    private async Task RefreshTotalsAsync()
    {
        int request = ++_totalsRequest;

        try
        {
            TotalConnected = ValidateTotalConnected();

            string? storage = await ValidateTotalStorageAsync();

            if (storage is not null && request == _totalsRequest)
            {
                TotalStorage = storage;
            }
        }
        catch (Exception ex)
        {
            AppLog.For<HomeViewModel>().LogError(ex, "Failed to refresh the dashboard totals.");
        }
    }

    private async Task<string?> ValidateTotalStorageAsync()
    {
        HashSet<string> liveLetters = _nasConnector.GetLiveLetters();

        string[] connected = [.. _allDrives
            .Where(d => d.IsUp(_nasConnector, liveLetters))
            .Select(d => d.Letter)];

        IReadOnlyList<VolumeUsage> volumes = await _storageProbe.ProbeAsync(connected);

        if (connected.Length > 0 && volumes.Count == 0)
        {
            return null;
        }

        return StorageUsageHelper.FormatCombined(
            volumes.Sum(volume => volume.UsedBytes),
            volumes.Sum(volume => volume.TotalBytes));
    }

    private string ValidateTotalConnected()
    {
        SyncConnectivity(_nasConnector.GetLiveLetters());

        int count = _allDrives.Count(d => d.Connected);

        ConnectedCount = count;
        DisconnectedCount = _allDrives.Count - count;

        return $"{count} / {_allDrives.Count}";
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<CheckDrivesStatusMessage>(this, (r, m) =>
        {
            _ = RefreshTotalsAsync();
        });

        WeakReferenceMessenger.Default.Register<DriveDeletedMessage>(this, (r, m) =>
        {
            DriveDisplay? existingDrive = _allDrives.FirstOrDefault(d => d.Id == m.DriveId);
            if (existingDrive is not null)
            {
                _allDrives.Remove(existingDrive);

                ApplyFilterAndSort();

                _ = RefreshTotalsAsync();

                _ = FetchDriveGroupsAsync();
            }
        });

        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(this, (r, m) =>
        {
            if (_allDrives.Any(d => d.Id == m.Drive.Id))
            {
                return;
            }

            _allDrives.Add(new DriveDisplay(m.Drive));

            ApplyFilterAndSort();

            _ = RefreshTotalsAsync();

            _ = FetchDriveGroupsAsync();
        });

        WeakReferenceMessenger.Default.Register<DriveUpdatedMessage>(this, (r, m) =>
        {
            DriveDisplay? existingDrive = _allDrives.FirstOrDefault(d => d.Id == m.UpdatedDrive.Id);
            if (existingDrive is null)
            {
                return;
            }

            existingDrive.Letter = m.UpdatedDrive.Letter;
            existingDrive.Name = m.UpdatedDrive.Name;
            existingDrive.Host = m.UpdatedDrive.Host;
            existingDrive.RemoteHost = m.UpdatedDrive.RemoteHost;
            existingDrive.Protocol = m.UpdatedDrive.Protocol;
            existingDrive.Port = m.UpdatedDrive.Port;
            existingDrive.CanWake = m.UpdatedDrive.CanWake;

            SyncConnectivity(_nasConnector.GetLiveLetters());

            ApplyFilterAndSort();

            _ = RefreshTotalsAsync();
        });

        WeakReferenceMessenger.Default.Register<DriveGroupsChangedMessage>(this, (r, m) =>
        {
            _ = FetchDriveGroupsAsync();
        });

        WeakReferenceMessenger.Default.Register<StorageSamplesRecordedMessage>(this, (r, m) =>
        {
            _ = FetchStorageForecastsAsync();
        });

        WeakReferenceMessenger.Default.Register<DriveAttemptFailedMessage>(this, (r, m) =>
        {
            _allDrives.FirstOrDefault(d => d.Id == m.DriveId)
                ?.MarkOffline(Error.Problem(m.ErrorCode, m.Description));
        });

        WeakReferenceMessenger.Default.Register<DriveAttemptSucceededMessage>(this, (r, m) =>
        {
            DriveDisplay? drive = _allDrives.FirstOrDefault(d => d.Id == m.DriveId);
            if (drive is null)
            {
                return;
            }

            drive.Connected = true;
            drive.LastConnectedOnUtc = m.ConnectedOnUtc;
        });
    }
}

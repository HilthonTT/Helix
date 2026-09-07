using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.DriveGroups;
using Helix.App.Messaging.Drives;
using Helix.App.Messaging.Navigation;
using Helix.App.Icons;
using Helix.App.Models;
using Helix.Application.Core.Sorting;
using Microsoft.Extensions.Logging;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Storage;
using Helix.Application.Features.DriveGroups.Queries;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
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

        RegisterMessages();
        InitializeCountdownEvents();

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

    [RelayCommand]
    private static void OpenDriveGroupsModal()
    {
        WeakReferenceMessenger.Default.Send(new DriveGroupsMessage(true));
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

        Drives = new(visible);

        OnPropertyChanged(nameof(HasSearchTerm));
        OnPropertyChanged(nameof(ShowNoDrives));
        OnPropertyChanged(nameof(ShowNoMatches));
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
        Result<List<Drive>> result = await ScopedHandler.HandleAsync((ImportDrives h) => h.Handle());
        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        await DisplaySuccessAsync(AppResources.DrivesImported);

        List<Drive> drives = result.Value;

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

        if (disconnect && !await ConfirmDisconnectAsync(selected.Count(drive => drive.Connected)))
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

    private static async Task<bool> ConfirmDisconnectAsync(int connectedCount)
    {
        if (connectedCount == 0)
        {
            return true;
        }

        string message = connectedCount == 1
            ? AppResources.DisconnectConfirmOne
            : string.Format(AppResources.DisconnectConfirmMany, connectedCount);

        return await Shell.Current.DisplayAlertAsync(
            AppResources.DisconnectConfirmTitle,
            message,
            AppResources.Disconnect,
            AppResources.Cancel);
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

        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();

        if (!await ConfirmDisconnectAsync(_allDrives.Count(drive => connectedLetters.Contains(drive.Letter))))
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

        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();
        if (_allDrives.All(d => connectedLetters.Contains(d.Letter)))
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

        _allDrives.Clear();
        _allDrives.AddRange(drives.Select(d => new DriveDisplay(d)));

        SyncConnectivity(_nasConnector.GetConnectedLetters());

        ApplyFilterAndSort();

        await RefreshTotalsAsync();

        return drives;
    }

    private void SyncConnectivity(HashSet<string> connectedLetters)
    {
        foreach (DriveDisplay drive in _allDrives)
        {
            drive.Connected = connectedLetters.Contains(drive.Letter);
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

    private async Task RefreshTotalsAsync()
    {
        try
        {
            TotalConnected = ValidateTotalConnected();
            TotalStorage = await ValidateTotalStorageAsync();
        }
        catch (Exception ex)
        {
            AppLog.For<HomeViewModel>().LogError(ex, "Failed to refresh the dashboard totals.");
        }
    }

    private async Task<string> ValidateTotalStorageAsync()
    {
        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();

        string[] connected = [.. _allDrives
            .Where(d => connectedLetters.Contains(d.Letter))
            .Select(d => d.Letter)];

        IReadOnlyList<VolumeUsage> volumes = await _storageProbe.ProbeAsync(connected);

        return StorageUsageHelper.FormatCombined(
            volumes.Sum(volume => volume.UsedBytes),
            volumes.Sum(volume => volume.TotalBytes));
    }

    private string ValidateTotalConnected()
    {
        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();

        SyncConnectivity(connectedLetters);

        int count = _allDrives.Count(d => connectedLetters.Contains(d.Letter));

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

            SyncConnectivity(_nasConnector.GetConnectedLetters());

            ApplyFilterAndSort();

            _ = RefreshTotalsAsync();
        });

        WeakReferenceMessenger.Default.Register<DriveGroupsChangedMessage>(this, (r, m) =>
        {
            _ = FetchDriveGroupsAsync();
        });

        WeakReferenceMessenger.Default.Register<DriveAttemptFailedMessage>(this, (r, m) =>
        {
            _allDrives.FirstOrDefault(d => d.Id == m.DriveId)
                ?.MarkOffline(Error.Problem(m.ErrorCode, m.Description));
        });
    }
}

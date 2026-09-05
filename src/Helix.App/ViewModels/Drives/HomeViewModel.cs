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

        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Drives = [];
        DriveGroups = [];
        SearchTerm = string.Empty;
        SortField = DriveSortField.Letter;
        SortOrder = SortOrder.Ascending;
        TotalStorage = string.Empty;
        TotalConnected = string.Empty;

        RegisterMessages();
        InitializeCountdownEvents();

        // No WatchSelection call here: seeding Drives above already ran OnDrivesChanged,
        // and subscribing a second time would double every notification.
    }

    /// <summary>
    /// Keeps the selection-derived properties in step with the rows.
    /// </summary>
    /// <remarks>
    /// Two things can move underneath them: the collection itself is replaced wholesale by
    /// a reload or a search, and rows are added and removed one at a time by the create
    /// and delete messages. Subscribing here rather than at each of those sites is what
    /// stops the next one that is added from forgetting to.
    /// </remarks>
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

    /// <summary>
    /// Re-subscribes when the whole collection is swapped out — a search, or a reload.
    /// </summary>
    /// <remarks>
    /// The old rows are dropped along with the collection holding them, so their
    /// subscriptions go with it; nothing else references them.
    /// </remarks>
    partial void OnDrivesChanged(ObservableCollection<DriveDisplay> value) => WatchSelection(value);

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

    /// <summary>
    /// Every drive the user has, whatever the filter is showing.
    /// </summary>
    /// <remarks>
    /// <see cref="Drives"/> is the view onto this: filtered, sorted, and rebuilt from
    /// these same row objects rather than from fresh ones, so what a row is holding — its
    /// tick, a mount in flight, why it is offline — survives typing in the search box.
    /// </remarks>
    private readonly List<DriveDisplay> _allDrives = [];

    /// <summary>What the list is showing: <see cref="_allDrives"/> filtered and sorted.</summary>
    [ObservableProperty]
    public partial ObservableCollection<DriveDisplay> Drives { get; set; }

    /// <summary>The rows currently ticked.</summary>
    /// <remarks>
    /// Read off the rows rather than kept as a second list beside them. A drive deleted
    /// from under a selection then simply is not in it, with nothing to keep in step.
    /// </remarks>
    public IReadOnlyList<DriveDisplay> SelectedDrives => [.. Drives.Where(drive => drive.IsSelected)];

    public bool HasSelection => Drives.Any(drive => drive.IsSelected);

    /// <summary>
    /// Whether the header tick is filled: every row, and at least one row.
    /// </summary>
    public bool AllSelected => Drives.Count > 0 && Drives.All(drive => drive.IsSelected);

    public string SelectionSummary => string.Format(AppResources.DrivesSelected, SelectedDrives.Count);

    /// <summary>
    /// Whether the drive card is too narrow to show everything with its label on.
    /// </summary>
    /// <remarks>
    /// Set by the page from the card's measured width, because MAUI has no media queries
    /// and the card can be 670 DIPs wide in an ordinary un-maximized window — the right
    /// column takes 320 for the connectivity chart before the list sees any. At that
    /// width the header's title, search box and three labelled chips wanted ~810, and
    /// the row's fixed columns wanted more than the row had, so the one flexible column
    /// — the drive's name — was the one that got nothing.
    ///
    /// Compact drops the chip labels for their icons and tooltips, and collapses the
    /// storage-usage column, which reads "Drive not ready" for every drive that is down
    /// and is the least useful thing on the row when space is short.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChipLabels))]
    public partial bool IsCompact { get; set; }

    /// <summary>
    /// The column grids do not bind through this viewmodel — a row's ColumnDefinition
    /// cannot reach it — so the flag is mirrored into <see cref="DriveListLayout"/>,
    /// which both grids bind to directly.
    /// </summary>
    partial void OnIsCompactChanged(bool value) => DriveListLayout.Instance.IsCompact = value;

    public bool ShowChipLabels => !IsCompact;

    /// <summary>
    /// The saved sets of drives, as the strip above the drive list shows them.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<DriveGroupDisplay> DriveGroups { get; set; }

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
    private static void OpenDriveGroupsModal()
    {
        WeakReferenceMessenger.Default.Send(new DriveGroupsMessage(true));
    }

    /// <summary>
    /// The live filter, applied as the user types.
    /// </summary>
    /// <remarks>
    /// It used to be a modal: open a sheet, type, press Search, wait for a database round
    /// trip, and have the results replace the list. Four interactions and a query to
    /// narrow a list of thirteen rows that is already in memory. Matching here instead is
    /// instant, shows the list narrowing as you type, and — the part the query could not
    /// do at all — matches the drive's host as well as its name and letter.
    /// </remarks>
    [ObservableProperty]
    public partial string SearchTerm { get; set; }

    partial void OnSearchTermChanged(string value) => ApplyFilterAndSort();

    /// <summary>The column the list is ordered by, and which way.</summary>
    [ObservableProperty]
    public partial DriveSortField SortField { get; set; }

    [ObservableProperty]
    public partial SortOrder SortOrder { get; set; }

    public bool HasSearchTerm => !string.IsNullOrEmpty(SearchTerm);

    /// <summary>
    /// The user has no drives at all — the first-run state, which offers to add one.
    /// </summary>
    public bool ShowNoDrives => Drives.Count == 0 && !HasSearchTerm;

    /// <summary>
    /// They have drives; none of them match what is in the search box.
    /// </summary>
    /// <remarks>
    /// Its own state rather than sharing the empty one. Filtering thirteen drives down to
    /// none and being told "you have no drives yet — add one" is the app forgetting what
    /// the user just typed.
    /// </remarks>
    public bool ShowNoMatches => Drives.Count == 0 && HasSearchTerm;

    /// <summary>
    /// Sorts by <paramref name="field"/>, or reverses it if the list is on that column
    /// already.
    /// </summary>
    /// <remarks>
    /// A fresh column starts ascending rather than keeping the previous direction: it is
    /// the answer people expect from a header click, and it makes the caret the only thing
    /// they have to read to know where they are.
    /// </remarks>
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

    /// <summary>
    /// Rebuilds the visible list from <see cref="_allDrives"/>.
    /// </summary>
    /// <remarks>
    /// The rows themselves are reused rather than rebuilt, which is what lets a selection,
    /// a busy spinner and a drive's offline reason survive typing in the search box.
    ///
    /// Anything filtered out is deselected on the way. Acting on a ticked row that is not
    /// on screen is the one outcome worth ruling out here — "disconnect" has to mean the
    /// rows the user can see.
    /// </remarks>
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
            // Letter second in both of the others, so drives that tie on the first key
            // keep a stable order instead of shuffling on every keystroke.
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

    /// <summary>Nudges the header carets, which are computed from the two sort properties.</summary>
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

    /// <summary>The caret drawn against whichever column is currently sorted.</summary>
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
            // Announce each one rather than adding it to Drives directly: DriveWatchdog
            // rebuilds its watch set from this message, and adding the rows by hand left
            // freshly imported drives unmonitored — no connectivity refresh and no
            // auto-reconnect — until the next sign-in. The registration below is what
            // puts them on screen.
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
        // Anything short of everything means "select the rest"; only a full list clears.
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

    /// <summary>
    /// Mounts or unmounts the ticked rows, then leaves the selection alone.
    /// </summary>
    /// <remarks>
    /// Deliberately not cleared afterwards: connecting three drives and then wanting to
    /// disconnect the same three is the common second act, and re-ticking them by hand
    /// would be the app forgetting what the user just told it.
    /// </remarks>
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

    /// <summary>
    /// Asks before unmounting more than one drive at once.
    /// </summary>
    /// <remarks>
    /// A dialog rather than a banner, because this is the one case on the page that is a
    /// question rather than a result. Deleting a single drive was confirmed and unmounting
    /// thirteen at a stroke was not, which had the risk backwards: an unmount pulls the
    /// filesystem out from under whatever has a file open on it.
    ///
    /// Skipped when nothing would actually come down — a selection of already-disconnected
    /// drives is a no-op, and a confirmation for a no-op teaches the user to dismiss them
    /// unread.
    /// </remarks>
    private static async Task<bool> ConfirmDisconnectAsync(int connectedCount)
    {
        if (connectedCount == 0)
        {
            return true;
        }

        // Two forms rather than one with a number in it: "1 drives will be disconnected"
        // is the sort of thing a translated string should not be made to say.
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

            // ConnectAllDrives.Handle is already fully async — no need to offload via Task.Run.
            // No OnlyAutoConnect here: the user pressed the button, so drives held back
            // from the automatic passes are still meant to come up.
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

        // Counted over every drive, because DisconnectAllDrives is going to take down
        // every drive — including any the filter is hiding.
        if (!await ConfirmDisconnectAsync(_allDrives.Count(drive => drive.Connected)))
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
        if (_allDrives.All(d => connectedLetters.Contains(d.Letter)))
        {
            return;
        }

        // ConnectAllDrives.Handle internally runs Task.WhenAll across all disconnected
        // drives — much faster than the previous serial per-drive message dispatch.
        // OnlyAutoConnect: this pass is unattended, so each drive's own flag decides
        // whether it takes part.
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

        ApplyFilterAndSort();

        await RefreshTotalsAsync();

        return drives;
    }

    /// <summary>
    /// Re-reads the groups behind the strip.
    /// </summary>
    /// <remarks>
    /// Counted against the drives on screen so a group that names a deleted drive shows
    /// what it can still connect rather than what it once held. Failures are swallowed on
    /// purpose: a strip that cannot be read is a strip that is not shown, and it must not
    /// stand between the user and the drive list underneath it.
    /// </remarks>
    public async Task FetchDriveGroupsAsync()
    {
        Result<List<DriveGroup>> result = await ScopedHandler.HandleAsync((GetDriveGroups h) => h.Handle());
        if (result.IsFailure)
        {
            DriveGroups = [];

            return;
        }

        // Resolved against every drive, not the visible ones: a filter must not make a
        // group look like it has lost members.
        HashSet<Guid> existing = [.. _allDrives.Select(drive => drive.Id)];

        DriveGroups = [.. result.Value.Select(group => new DriveGroupDisplay(group, existing))];
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
            AppLog.For<HomeViewModel>().LogError(ex, "Failed to refresh the dashboard totals.");
        }
    }

    /// <summary>
    /// Capacity across every connected drive, counting each underlying volume once.
    /// </summary>
    /// <remarks>
    /// The tile has always been labelled total storage but used to read a single drive,
    /// so a second NAS simply did not appear in the figure. Summing the letters instead
    /// is just as wrong the other way: mapped drives are usually several shares of one
    /// NAS, each reporting that one pool's full size, so a 43 TB server mapped three
    /// times read as 129 TB. <see cref="IStorageProbe"/> resolves what each mount is
    /// actually on and returns one reading per volume; this only has to add them up.
    /// </remarks>
    private async Task<string> ValidateTotalStorageAsync()
    {
        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();

        // Every drive, not the filtered view. The dashboard tiles report what the user
        // has; a search box narrowing the list below them is not the NAS getting smaller.
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
            // Looked up in the master list, not the visible one: a drive can be deleted
            // while a filter is hiding it.
            DriveDisplay? existingDrive = _allDrives.FirstOrDefault(d => d.Id == m.DriveId);
            if (existingDrive is not null)
            {
                _allDrives.Remove(existingDrive);

                ApplyFilterAndSort();

                _ = RefreshTotalsAsync();

                // A group that named it is now one drive smaller. Without this the chip
                // goes on claiming a drive that no longer exists until the page is
                // reloaded, and connecting the group quietly does less than it says.
                _ = FetchDriveGroupsAsync();
            }
        });

        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(this, (r, m) =>
        {
            _allDrives.Add(new DriveDisplay(m.Drive));

            // Re-projected rather than appended, so an arriving drive lands in sort order
            // and is hidden if it does not match the filter that is up.
            ApplyFilterAndSort();

            _ = RefreshTotalsAsync();

            // Counted against the drives on screen, so a drive arriving changes what an
            // existing group can resolve — an import is the case that matters.
            _ = FetchDriveGroupsAsync();
        });

        WeakReferenceMessenger.Default.Register<DriveGroupsChangedMessage>(this, (r, m) =>
        {
            _ = FetchDriveGroupsAsync();
        });
    }
}

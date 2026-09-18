using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Icons;
using Helix.App.Models;
using Helix.Application.Core.Sorting;
using Helix.Application.Features.Auditlogs.Contracts;
using Helix.Application.Features.Auditlogs.Queries;
using Helix.Domain.Auditlogs;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Auditlogs;

internal sealed partial class AuditlogsViewModel : BaseViewModel
{
    private readonly List<AuditlogDisplay> _allAuditlogs = [];

    private readonly HashSet<Guid> _loadedIds = [];

    private bool _loading;

    private bool _exhausted;

    private bool _hasLoaded;

    private int _totalCount;

    public AuditlogsViewModel()
    {
        Auditlogs = [];
        SearchTerm = string.Empty;
        SortOrder = SortOrder.Descending;
    }

    [ObservableProperty]
    public partial ObservableCollection<AuditlogDisplay> Auditlogs { get; set; }

    [ObservableProperty]
    public partial string SearchTerm { get; set; }

    partial void OnSearchTermChanged(string value)
    {
        ApplyFilterAndSort();

        // The sentence a row shows is composed here, in the user's language, and is not
        // in the database — so a search can only be answered over rows that are loaded.
        // Typing therefore pulls the rest of the history in, once, behind the matches
        // the loaded pages can already show.
        if (!string.IsNullOrEmpty(value.Trim()) && HasMore)
        {
            _ = WidenSearchAsync();
        }
    }

    private async Task WidenSearchAsync()
    {
        try
        {
            await LoadRestAsync();

            ApplyFilterAndSort();
        }
        catch (Exception ex)
        {
            AppLog.For<AuditlogsViewModel>().LogWarning(ex, "The rest of the history could not be loaded to search it.");
        }
    }

    [ObservableProperty]
    public partial SortOrder SortOrder { get; set; }

    public bool HasSearchTerm => !string.IsNullOrEmpty(SearchTerm);

    public bool HasMore => !_exhausted && _allAuditlogs.Count < _totalCount;

    public bool ShowNoAuditlogs => _hasLoaded && Auditlogs.Count == 0 && !HasSearchTerm;

    public bool ShowNoMatches => _hasLoaded && Auditlogs.Count == 0 && HasSearchTerm;

    /// <summary>
    /// What the page reports it is holding: the whole history while the list is merely
    /// scrolled part of the way through it, and the matches once a search narrows it.
    /// </summary>
    public int CountDisplay => HasSearchTerm ? Auditlogs.Count : _totalCount;

    public string SortGlyph => SortOrder == SortOrder.Ascending ? IconFont.CaretUp : IconFont.CaretDown;

    [RelayCommand]
    private async Task ToggleSortOrderAsync()
    {
        SortOrder = SortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;

        OnPropertyChanged(nameof(SortGlyph));

        if (_loading)
        {
            _reloadRequested = true;

            return;
        }

        // The order is the database's, not the loaded page's, so it has to be asked again
        // from the top: sorting thirteen loaded rows of ninety days ascending would put the
        // newest rows in the oldest order and call it the oldest history.
        await GetAuditlogsAsync();
    }

    private bool _reloadRequested;

    private Task ReloadIfRequestedAsync()
    {
        if (!_reloadRequested || _loading)
        {
            return Task.CompletedTask;
        }

        _reloadRequested = false;

        return GetAuditlogsAsync();
    }

    [RelayCommand]
    private void ClearSearch() => SearchTerm = string.Empty;

    [RelayCommand]
    private async Task GetAuditlogsAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        IsBusy = true;

        try
        {
            _allAuditlogs.Clear();
            _loadedIds.Clear();
            _exhausted = false;
            _totalCount = 0;

            bool loaded = await LoadPageAsync(skip: 0) is not null;

            _hasLoaded = _hasLoaded || loaded;

            ApplyFilterAndSort();

            // A search that survived a reload still has to see the whole history.
            if (HasSearchTerm && HasMore)
            {
                _loading = false;

                await LoadRestAsync();

                ApplyFilterAndSort();
            }
        }
        finally
        {
            _loading = false;
            IsBusy = false;
        }

        await ReloadIfRequestedAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_loading || !HasMore)
        {
            return;
        }

        _loading = true;

        try
        {
            if (await LoadPageAsync(_allAuditlogs.Count) is not null)
            {
                ApplyFilterAndSort();
            }
        }
        finally
        {
            _loading = false;
        }

        await ReloadIfRequestedAsync();
    }

    private async Task LoadRestAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        IsBusy = true;

        try
        {
            while (HasMore)
            {
                if (await LoadPageAsync(_allAuditlogs.Count, GetAuditlogs.MaximumPageSize) is null)
                {
                    break;
                }
            }
        }
        finally
        {
            _loading = false;
            IsBusy = false;
        }

        await ReloadIfRequestedAsync();
    }

    private async Task<int?> LoadPageAsync(int skip, int take = GetAuditlogs.DefaultPageSize)
    {
        SortOrder order = SortOrder;

        Result<AuditlogPage> result = await ScopedHandler.HandleAsync((GetAuditlogs h) =>
            h.Handle(new GetAuditlogs.Request(skip, take, order)));

        if (result.IsFailure || order != SortOrder)
        {
            return null;
        }

        AuditlogPage page = result.Value;

        _totalCount = page.TotalCount;

        int added = 0;

        foreach (Auditlog item in page.Items)
        {
            if (!_loadedIds.Add(item.Id))
            {
                continue;
            }

            _allAuditlogs.Add(new AuditlogDisplay(item));

            added++;
        }

        if (added == 0)
        {
            _exhausted = true;
        }

        return added;
    }

    private void ApplyFilterAndSort()
    {
        string term = SearchTerm.Trim();

        IEnumerable<AuditlogDisplay> matching = string.IsNullOrEmpty(term)
            ? _allAuditlogs
            : _allAuditlogs.Where(log => Matches(log, term));

        AuditlogDisplay[] wanted = [.. SortOrder == SortOrder.Ascending
            ? matching.OrderBy(log => log.CreatedOnUtc)
            : matching.OrderByDescending(log => log.CreatedOnUtc)];

        if (!TryAppend(Auditlogs, wanted))
        {
            Auditlogs = new(wanted);
        }

        OnPropertyChanged(nameof(HasSearchTerm));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CountDisplay));
        OnPropertyChanged(nameof(ShowNoAuditlogs));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    private static bool TryAppend(ObservableCollection<AuditlogDisplay> shown, AuditlogDisplay[] wanted)
    {
        if (shown.Count == 0 || wanted.Length < shown.Count)
        {
            return false;
        }

        for (int i = 0; i < shown.Count; i++)
        {
            if (!ReferenceEquals(shown[i], wanted[i]))
            {
                return false;
            }
        }

        for (int i = shown.Count; i < wanted.Length; i++)
        {
            shown.Add(wanted[i]);
        }

        return true;
    }

    private static bool Matches(AuditlogDisplay log, string term) =>
        log.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        (log.EntityLetter?.Equals(term, StringComparison.OrdinalIgnoreCase) ?? false);
}

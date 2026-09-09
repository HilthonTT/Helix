using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Icons;
using Helix.App.Models;
using Helix.Application.Core.Sorting;
using Helix.Application.Features.Auditlogs.Contracts;
using Helix.Application.Features.Auditlogs.Queries;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Auditlogs;

internal sealed partial class AuditlogsViewModel : BaseViewModel
{
    private readonly List<AuditlogDisplay> _allAuditlogs = [];

    private bool _loading;

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

    public bool HasMore => _allAuditlogs.Count < _totalCount;

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

        // The order is the database's, not the loaded page's, so it has to be asked again
        // from the top: sorting thirteen loaded rows of ninety days ascending would put the
        // newest rows in the oldest order and call it the oldest history.
        await GetAuditlogsAsync();
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
            _totalCount = 0;

            bool loaded = await LoadPageAsync(skip: 0);

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
            if (await LoadPageAsync(_allAuditlogs.Count))
            {
                ApplyFilterAndSort();
            }
        }
        finally
        {
            _loading = false;
        }
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
            // Bounded by the total the first page reported, so a history that is being
            // written to while it is read cannot turn this into an endless loop.
            while (HasMore)
            {
                if (!await LoadPageAsync(_allAuditlogs.Count, GetAuditlogs.MaximumPageSize))
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
    }

    private async Task<bool> LoadPageAsync(int skip, int take = GetAuditlogs.DefaultPageSize)
    {
        Result<AuditlogPage> result = await ScopedHandler.HandleAsync((GetAuditlogs h) =>
            h.Handle(new GetAuditlogs.Request(skip, take, SortOrder)));

        if (result.IsFailure)
        {
            return false;
        }

        AuditlogPage page = result.Value;

        _totalCount = page.TotalCount;

        if (page.Items.Count == 0)
        {
            return true;
        }

        _allAuditlogs.AddRange(page.Items.Select(a => new AuditlogDisplay(a)));

        return true;
    }

    private void ApplyFilterAndSort()
    {
        string term = SearchTerm.Trim();

        IEnumerable<AuditlogDisplay> matching = string.IsNullOrEmpty(term)
            ? _allAuditlogs
            : _allAuditlogs.Where(log => Matches(log, term));

        Auditlogs = new(SortOrder == SortOrder.Ascending
            ? matching.OrderBy(log => log.CreatedOnUtc)
            : matching.OrderByDescending(log => log.CreatedOnUtc));

        OnPropertyChanged(nameof(HasSearchTerm));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CountDisplay));
        OnPropertyChanged(nameof(ShowNoAuditlogs));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    private static bool Matches(AuditlogDisplay log, string term) =>
        log.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        (log.EntityLetter?.Equals(term, StringComparison.OrdinalIgnoreCase) ?? false);
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Icons;
using Helix.App.Models;
using Helix.Application.Core.Sorting;
using Helix.Application.Features.Auditlogs.Queries;
using Helix.Domain.Auditlogs;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Auditlogs;

internal sealed partial class AuditlogsViewModel : BaseViewModel
{
    private readonly List<AuditlogDisplay> _allAuditlogs = [];

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

    partial void OnSearchTermChanged(string value) => ApplyFilterAndSort();

    [ObservableProperty]
    public partial SortOrder SortOrder { get; set; }

    public bool HasSearchTerm => !string.IsNullOrEmpty(SearchTerm);

    public bool ShowNoAuditlogs => Auditlogs.Count == 0 && !HasSearchTerm;

    public bool ShowNoMatches => Auditlogs.Count == 0 && HasSearchTerm;

    public string SortGlyph => SortOrder == SortOrder.Ascending ? IconFont.CaretUp : IconFont.CaretDown;

    [RelayCommand]
    private void ToggleSortOrder()
    {
        SortOrder = SortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;

        OnPropertyChanged(nameof(SortGlyph));

        ApplyFilterAndSort();
    }

    [RelayCommand]
    private void ClearSearch() => SearchTerm = string.Empty;

    [RelayCommand]
    private async Task GetAuditlogsAsync()
    {
        Result<List<Auditlog>> result = await ScopedHandler.HandleAsync((GetAuditlogs h) => h.Handle());
        if (result.IsSuccess)
        {
            _allAuditlogs.Clear();
            _allAuditlogs.AddRange(result.Value.Select(a => new AuditlogDisplay(a)));

            ApplyFilterAndSort();
        }
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
        OnPropertyChanged(nameof(ShowNoAuditlogs));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    private static bool Matches(AuditlogDisplay log, string term) =>
        log.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        (log.EntityLetter?.Equals(term, StringComparison.OrdinalIgnoreCase) ?? false);
}

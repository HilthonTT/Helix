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
    /// <summary>
    /// Every entry that was loaded, whatever the filter is showing.
    /// </summary>
    private readonly List<AuditlogDisplay> _allAuditlogs = [];

    public AuditlogsViewModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Auditlogs = [];
        SearchTerm = string.Empty;
        SortOrder = SortOrder.Descending;
    }

    /// <summary>What the list is showing: <see cref="_allAuditlogs"/> filtered and sorted.</summary>
    [ObservableProperty]
    public partial ObservableCollection<AuditlogDisplay> Auditlogs { get; set; }

    /// <summary>
    /// The live filter, applied as the user types.
    /// </summary>
    /// <remarks>
    /// Matched against the sentence the row actually shows, which the query it replaced
    /// could not do: the sentence is composed at display time in the user's language and
    /// does not exist in the database. Searching for "disconnected" used to find nothing
    /// while the page was full of the word.
    /// </remarks>
    [ObservableProperty]
    public partial string SearchTerm { get; set; }

    partial void OnSearchTermChanged(string value) => ApplyFilterAndSort();

    /// <summary>
    /// Newest first by default, which is the end of a log anyone opening it wants.
    /// </summary>
    [ObservableProperty]
    public partial SortOrder SortOrder { get; set; }

    public bool HasSearchTerm => !string.IsNullOrEmpty(SearchTerm);

    public bool ShowNoAuditlogs => Auditlogs.Count == 0 && !HasSearchTerm;

    public bool ShowNoMatches => Auditlogs.Count == 0 && HasSearchTerm;

    /// <summary>The caret on the date column.</summary>
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

    /// <summary>
    /// Matches the rendered sentence, and the drive letter behind it.
    /// </summary>
    /// <remarks>
    /// The letter is matched separately because a one-character term is worth honouring
    /// against it and the sentence would swallow it — "Z" appears in plenty of words.
    /// </remarks>
    private static bool Matches(AuditlogDisplay log, string term) =>
        log.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        (log.EntityLetter?.Equals(term, StringComparison.OrdinalIgnoreCase) ?? false);
}

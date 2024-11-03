using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Models;
using Helix.App.Pages;
using Helix.Application.Auditlogs;
using Helix.Application.Core.Sorting;
using Helix.Domain.Auditlogs;
using SharedKernel;

namespace Helix.App.Modals.Auditlogs.Search;

internal sealed partial class SearchAuditlogsViewModel : BaseViewModel
{
    private readonly SearchAuditlogs _searchAuditlogs;

    public SearchAuditlogsViewModel()
    {
        _searchAuditlogs = App.ServiceProvider.GetRequiredService<SearchAuditlogs>();
    }

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private SortOrder _sortOrder = SortOrder.Ascending;

    [ObservableProperty]
    private string _sortOrderString = "Ascending";
    partial void OnSortOrderStringChanged(string value)
    {
        if (value == "Ascending")
        {
            SortOrder = SortOrder.Ascending;
        }
        else
        {
            SortOrder = SortOrder.Descending;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new SearchAuditlogs.Request(SearchTerm, SortOrder);

            Result<List<Auditlog>> result = await _searchAuditlogs.Handle(request);

            if (result.IsSuccess)
            {
                WeakReferenceMessenger.Default.Send(new AuditlogsSearchedMessage(result.Value));
            }

            Close();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        SearchTerm = string.Empty;
        WeakReferenceMessenger.Default.Send(new SearchAuditlogsMessage(false));
    }
}

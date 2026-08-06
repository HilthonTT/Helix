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
    public SearchAuditlogsViewModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        SearchTerm = string.Empty;
        SortOrder = SortOrder.Ascending;
        SortOrderString = "Ascending";
    }

    [ObservableProperty]
    public partial string SearchTerm { get; set; }

    [ObservableProperty]
    public partial SortOrder SortOrder { get; set; }

    [ObservableProperty]
    public partial string SortOrderString { get; set; }
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

            Result<List<Auditlog>> result = await ScopedHandler.HandleAsync((SearchAuditlogs h) => h.Handle(request));

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

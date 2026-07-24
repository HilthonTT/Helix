using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Pages;
using Helix.Application.Core.Sorting;
using Helix.Application.Drives;
using Helix.Domain.Drives;
using SharedKernel;

namespace Helix.App.Modals.Drives.Search;

internal sealed partial class SearchDrivesViewModel : BaseViewModel
{
    public SearchDrivesViewModel()
    {
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

            var request = new SearchDrives.Request(SearchTerm, SortOrder);

            Result<List<Drive>> result = await ScopedHandler.HandleAsync((SearchDrives h) => h.Handle(request));

            if (result.IsSuccess)
            {
                WeakReferenceMessenger.Default.Send(new DriveSearchedMessage(result.Value));
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
        WeakReferenceMessenger.Default.Send(new SearchDrivesMessage(false));
    }
}

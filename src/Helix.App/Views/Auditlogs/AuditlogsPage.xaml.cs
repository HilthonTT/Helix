using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Auditlogs;
using Helix.App.Services;
using Helix.App.ViewModels.Auditlogs;

namespace Helix.App.Views.Auditlogs;

public sealed partial class AuditlogsPage : ContentPage
{
    private const string SearchAuditlogs = "search-auditlogs";

    private readonly AuditlogsViewModel _viewModel;
    private readonly ModalHost _modals;

    public AuditlogsPage()
    {
        InitializeComponent();

        _viewModel = new AuditlogsViewModel();

        BindingContext = _viewModel;

        _modals = new ModalHost(BlockScreen);
        _modals.Register(SearchAuditlogs, SearchAuditlogsLayout, SearchAuditlogsView);
        _modals.AttachEscapeToDismiss(this);

        RegisterMessages();
    }

    protected override async void OnAppearing()
    {
        try
        {
            if (_viewModel.GetAuditlogsCommand.CanExecute(null))
            {
                await _viewModel.GetAuditlogsCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Something went wrong!", ex.Message, "Ok");
        }
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<SearchAuditlogsMessage>(
            this, async (r, m) => await _modals.ToggleAsync(SearchAuditlogs, m.Value));
    }
}

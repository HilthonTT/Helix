using Helix.App.Services;
using Helix.App.ViewModels.Auditlogs;

namespace Helix.App.Views.Auditlogs;

public sealed partial class AuditlogsPage : ContentPage
{

    private readonly AuditlogsViewModel _viewModel;

    public AuditlogsPage()
    {
        InitializeComponent();

        _viewModel = new AuditlogsViewModel();

        BindingContext = _viewModel;
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
            Notifier.Error(ex.Message);
        }
    }
}

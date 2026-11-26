using Helix.App.Resources.Languages;
using Helix.App.Services;
using Helix.App.ViewModels.Auditlogs;
using Microsoft.Extensions.Logging;

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
            AppLog.For<AuditlogsPage>().LogError(ex, "Loading the audit log failed.");

            Notifier.Error(AppResources.UnexpectedError);
        }
    }
}

namespace Helix.App.Pages.Auditlogs;

public sealed partial class AuditlogsPage : ContentPage
{
	public AuditlogsPage()
	{
		InitializeComponent();

		BindingContext = new AuditlogsViewModel();
	}

    protected override async void OnAppearing()
    {
		if (BindingContext is not AuditlogsViewModel viewModel)
		{
			return;
		}

		if (viewModel.GetAuditlogsCommand.CanExecute(null))
		{
			await viewModel.GetAuditlogsCommand.ExecuteAsync(null);
		}
    }
}
namespace Helix.App.Pages.Auditlogs;

public sealed partial class AuditlogsPage : ContentPage
{
	public AuditlogsPage()
	{
		InitializeComponent();

		BindingContext = new AuditlogsViewModel();
	}
}
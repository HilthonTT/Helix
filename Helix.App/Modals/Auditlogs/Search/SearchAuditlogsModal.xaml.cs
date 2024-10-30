namespace Helix.App.Modals.Auditlogs.Search;

public sealed partial class SearchAuditlogsModal : ContentView
{
	public SearchAuditlogsModal()
	{
		InitializeComponent();

		BindingContext = new SearchAuditlogsViewModel();
	}
}
using Helix.App.ViewModels.Auditlogs;

namespace Helix.App.Views.Auditlogs;

public sealed partial class SearchAuditlogsModal : ContentView
{
	public SearchAuditlogsModal()
	{
		InitializeComponent();

		BindingContext = new SearchAuditlogsViewModel();
	}
}
namespace Helix.App.Modals.Drives.Search;

public sealed partial class SearchDrivesModal : ContentView
{
	public SearchDrivesModal()
	{
		InitializeComponent();

		BindingContext = new SearchDrivesViewModel();
	}
}
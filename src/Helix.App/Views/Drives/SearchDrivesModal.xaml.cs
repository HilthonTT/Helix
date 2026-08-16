using Helix.App.ViewModels.Drives;

namespace Helix.App.Views.Drives;

public sealed partial class SearchDrivesModal : ContentView
{
	public SearchDrivesModal()
	{
		InitializeComponent();

		BindingContext = new SearchDrivesViewModel();
	}
}
using Helix.App.ViewModels.Drives;

namespace Helix.App.Views.Drives;

public sealed partial class CreateDriveModal : ContentView
{
	public CreateDriveModal()
	{
		InitializeComponent();

		BindingContext = new CreateDriveViewModel();
	}
}
using Helix.App.ViewModels.Drives;

namespace Helix.App.Views.Drives;

public sealed partial class UpdateDriveModal : ContentView
{
	public UpdateDriveModal()
	{
		InitializeComponent();

		BindingContext = new UpdateDriveViewModel();
	}
}
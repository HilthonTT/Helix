using Helix.App.ViewModels.Drives;

namespace Helix.App.Views.Drives;

public sealed partial class DeleteDriveModal : ContentView
{
	public DeleteDriveModal()
	{
		InitializeComponent();

		BindingContext = new DeleteDriveViewModel();
	}
}
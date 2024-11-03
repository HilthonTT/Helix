namespace Helix.App.Modals.Drives.Delete;

public sealed partial class DeleteDriveModal : ContentView
{
	public DeleteDriveModal()
	{
		InitializeComponent();

		BindingContext = new DeleteDriveViewModel();
	}
}
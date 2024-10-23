namespace Helix.App.Modals.Drives.Update;

public sealed partial class UpdateDriveModal : ContentView
{
	public UpdateDriveModal()
	{
		InitializeComponent();

		BindingContext = new UpdateDriveViewModel();
	}
}
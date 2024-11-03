namespace Helix.App.Modals.Drives.Create;

public sealed partial class CreateDriveModal : ContentView
{
	public CreateDriveModal()
	{
		InitializeComponent();

		BindingContext = new CreateDriveViewModel();
	}
}
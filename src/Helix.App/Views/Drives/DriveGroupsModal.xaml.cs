using Helix.App.ViewModels.Drives;

namespace Helix.App.Views.Drives;

public sealed partial class DriveGroupsModal : ContentView
{
	public DriveGroupsModal()
	{
		InitializeComponent();

		BindingContext = new DriveGroupsViewModel();
	}
}

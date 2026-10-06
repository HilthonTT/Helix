using Helix.App.ViewModels.Drives;

namespace Helix.App.Views.Drives;

public sealed partial class SchedulesModal : ContentView
{
	public SchedulesModal()
	{
		InitializeComponent();

		BindingContext = new SchedulesViewModel();
	}
}

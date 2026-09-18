using Helix.App.ViewModels.Drives;

namespace Helix.App.Views.Drives;

public sealed partial class AdoptMappingsModal : ContentView
{
	public AdoptMappingsModal()
	{
		InitializeComponent();

		BindingContext = new AdoptMappingsViewModel();
	}
}

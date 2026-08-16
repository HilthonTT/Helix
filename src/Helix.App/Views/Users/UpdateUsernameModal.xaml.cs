using Helix.App.ViewModels.Users;

namespace Helix.App.Views.Users;

public sealed partial class UpdateUsernameModal : ContentView
{
	public UpdateUsernameModal()
	{
		InitializeComponent();

		BindingContext = new UpdateUsernameViewModel();
	}
}
using Helix.App.ViewModels.Users;

namespace Helix.App.Views.Users;

public sealed partial class UpdatePasswordModal : ContentView
{
	public UpdatePasswordModal()
	{
		InitializeComponent();

		BindingContext = new UpdatePasswordViewModel();
	}
}
namespace Helix.App.Modals.Users.UpdatePassword;

public sealed partial class UpdatePasswordModal : ContentView
{
	public UpdatePasswordModal()
	{
		InitializeComponent();

		BindingContext = new UpdatePasswordViewModel();
	}
}
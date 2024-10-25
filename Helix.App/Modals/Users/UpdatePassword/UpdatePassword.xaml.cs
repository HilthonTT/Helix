namespace Helix.App.Modals.Users.UpdatePassword;

public sealed partial class UpdatePassword : ContentView
{
	public UpdatePassword()
	{
		InitializeComponent();

		BindingContext = new UpdatePasswordViewModel();
	}
}
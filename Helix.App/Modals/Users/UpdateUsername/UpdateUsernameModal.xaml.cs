namespace Helix.App.Modals.Users.UpdateUsername;

public sealed partial class UpdateUsernameModal : ContentView
{
	public UpdateUsernameModal()
	{
		InitializeComponent();

		BindingContext = new UpdateUsernameViewModel();
	}
}
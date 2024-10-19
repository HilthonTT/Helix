namespace Helix.App.Pages.Register;

public sealed partial class RegisterPage : ContentPage
{
	public RegisterPage()
	{
		InitializeComponent();

		BindingContext = new RegisterViewModel();
	}
}
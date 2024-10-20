namespace Helix.App.Pages.Home;

public sealed partial class HomePage : ContentPage
{
	public HomePage()
	{
		InitializeComponent();

		BindingContext = new HomeViewModel();
	}
}
namespace Helix.App.Pages.Settings;

public sealed partial class SettingsPage : ContentPage
{
	public SettingsPage()
	{
		InitializeComponent();

		BindingContext = new SettingsViewModel();
	}
}
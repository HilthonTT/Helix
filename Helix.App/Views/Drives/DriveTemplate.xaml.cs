using Helix.App.Models;

namespace Helix.App.Views.Drives;

public sealed partial class DriveTemplate : ContentView
{
	public DriveTemplate()
	{
		InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
		if (BindingContext is not DriveDisplay drive)
		{
			return;
		}

		Name.Text = drive.Name;
		Letter.Text = drive.Letter;
        StatusButton.BackgroundColor = Color.FromArgb(drive.ButtonColor);
    }

    private void ToggleConnect(object? sender, EventArgs e)
	{
		if (BindingContext is not DriveDisplay drive)
		{
			return;
		}
	}

	private void HandleUpdate(object? sender, TappedEventArgs e)
	{

	}

	private void HandleDelete(object? sender, TappedEventArgs e)
	{

	}
}
using Helix.App.ViewModels.Users;

namespace Helix.App.Views.Users;

public sealed partial class UpdatePasswordModal : ContentView
{
	private readonly UpdatePasswordViewModel _viewModel = new();

	public UpdatePasswordModal()
	{
		InitializeComponent();

		BindingContext = _viewModel;
	}

	public void Reset() => _viewModel.Reset();
}
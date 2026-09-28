using Helix.App.ViewModels.Users;

namespace Helix.App.Views.Users;

public sealed partial class CreateRecoveryKeyModal : ContentView
{
    private readonly CreateRecoveryKeyViewModel _viewModel = new();

    public CreateRecoveryKeyModal()
    {
        InitializeComponent();

        BindingContext = _viewModel;
    }

    public void Reset() => _viewModel.Reset();
}

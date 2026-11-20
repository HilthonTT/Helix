using Helix.App.ViewModels.Drives;

namespace Helix.App.Views.Drives;

public sealed partial class PassphraseModal : ContentView
{
    private readonly PassphraseViewModel _viewModel = new();

    public PassphraseModal()
    {
        InitializeComponent();

        BindingContext = _viewModel;
    }

    public Task<string?> AskAsync(bool confirm) => _viewModel.Ask(confirm);

    public void Cancel() => _viewModel.Cancel();

    public void FocusPassphrase() => PassphraseEntry.Focus();
}

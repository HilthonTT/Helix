using Helix.App.ViewModels.Users;

namespace Helix.App.Views.Users;

public sealed partial class LockPage : ContentPage
{
    private readonly LockViewModel _viewModel;

    public LockPage()
    {
        InitializeComponent();

        _viewModel = new LockViewModel();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _viewModel.Refresh();

        PasswordEntry.Text = string.Empty;
        PasswordEntry.Focus();
    }

    private void Password_Completed(object? sender, EventArgs e)
    {
        if (_viewModel.UnlockCommand.CanExecute(null))
        {
            _viewModel.UnlockCommand.Execute(null);
        }
    }
}

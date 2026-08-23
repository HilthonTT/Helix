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

        // Shell caches the page, so a second lock in the same session would otherwise
        // open showing the previous attempt's error and the name of whoever was signed
        // in when the page was first built.
        _viewModel.Refresh();

        PasswordEntry.Text = string.Empty;
        PasswordEntry.Focus();
    }

    /// <summary>Enter in the password box unlocks, as it does on the sign-in page.</summary>
    private void Password_Completed(object? sender, EventArgs e)
    {
        if (_viewModel.UnlockCommand.CanExecute(null))
        {
            _viewModel.UnlockCommand.Execute(null);
        }
    }
}

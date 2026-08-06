using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Modals.Users.UpdatePassword;
using Helix.App.Modals.Users.UpdateUsername;

namespace Helix.App.Pages.Settings;

public sealed partial class SettingsPage : ContentPage
{
    private static bool _updateUsernameModalOpen = false;
    private static bool _updatePasswordModalOpen = false;

    private readonly SettingsViewModel _viewModel;

	public SettingsPage()
	{
		InitializeComponent();

        _viewModel = new SettingsViewModel();

        BindingContext = _viewModel;

        RegisterMessages();
    }

    protected override async void OnAppearing()
    {
        try
        {
            await _viewModel.LoadSettingsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Something went wrong!", ex.Message, "Ok");
        }
    }

    private async Task ShowUpdatePasswordModalAsync(bool show)
    {
        if (_updateUsernameModalOpen)
        {
            await ShowUpdateUsernameModalAsync(false);
        }

        if (show)
        {
            OpenModalInternal(UpdatePasswordLayout, UpdatePasswordView);
            _updatePasswordModalOpen = true;
        }
        else
        {
            await CloseModalInternal(UpdatePasswordLayout, UpdatePasswordView);
            _updatePasswordModalOpen = false;
        }
    }

    private async Task ShowUpdateUsernameModalAsync(bool show)
    {
        if (_updatePasswordModalOpen)
        {
            await ShowUpdatePasswordModalAsync(false);
        }

        if (show)
        {
            OpenModalInternal(UpdateUsernameLayout, UpdateUsernameView);
            _updateUsernameModalOpen = true;
        }
        else
        {
            await CloseModalInternal(UpdateUsernameLayout, UpdateUsernameView);
            _updateUsernameModalOpen = false;
        }
    }

    private readonly Dictionary<AbsoluteLayout, int> _modalCloseTokens = [];

    private void OpenModalInternal(AbsoluteLayout absoluteLayout, ContentView contentView)
    {
        // Invalidate any pending close: its delayed hide would otherwise blank out a
        // modal that was reopened within the 800 ms close animation.
        _modalCloseTokens[absoluteLayout] = _modalCloseTokens.GetValueOrDefault(absoluteLayout) + 1;

        absoluteLayout.IsVisible = true;
        contentView.Opacity = 0;
        _ = contentView.FadeToAsync(1, 800, Easing.CubicIn);
        _ = BlockScreen.FadeToAsync(0.8, 800, Easing.CubicOut);

        BlockScreen.InputTransparent = false;
    }

    private async Task CloseModalInternal(AbsoluteLayout absoluteLayout, ContentView contentView)
    {
        int token = _modalCloseTokens.GetValueOrDefault(absoluteLayout) + 1;
        _modalCloseTokens[absoluteLayout] = token;

        _ = contentView.FadeToAsync(0, 800, Easing.CubicOut);
        _ = BlockScreen.FadeToAsync(0, 800, Easing.CubicOut);
        BlockScreen.InputTransparent = true;

        await Task.Delay(800);

        if (_modalCloseTokens.GetValueOrDefault(absoluteLayout) == token)
        {
            absoluteLayout.IsVisible = false;
        }
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<UpdateUsernameMessage>(this, async (r, m) =>
        {
            bool isAlreadyOpen = _updateUsernameModalOpen && m.Value;
            if (isAlreadyOpen)
            {
                return;
            }

            await ShowUpdateUsernameModalAsync(m.Value);
        });

        WeakReferenceMessenger.Default.Register<UpdatePasswordMessage>(this, async (r, m) =>
        {
            bool isAlreadyOpen = _updatePasswordModalOpen && m.Value;
            if (isAlreadyOpen)
            {
                return;
            }

            await ShowUpdatePasswordModalAsync(m.Value);
        });
    }
}
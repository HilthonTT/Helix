using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Users;
using Helix.App.Services;
using Helix.App.ViewModels.Settings;

namespace Helix.App.Views.Settings;

public sealed partial class SettingsPage : ContentPage
{
    private const string UpdateUsername = "update-username";
    private const string UpdatePassword = "update-password";

    private readonly SettingsViewModel _viewModel;
    private readonly ModalHost _modals;

    public SettingsPage()
    {
        InitializeComponent();

        _viewModel = new SettingsViewModel();

        BindingContext = _viewModel;

        _modals = new ModalHost(BlockScreen);
        _modals.Register(UpdateUsername, UpdateUsernameLayout, UpdateUsernameView);
        _modals.Register(UpdatePassword, UpdatePasswordLayout, UpdatePasswordView);
        _modals.AttachEscapeToDismiss(this);

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

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<UpdateUsernameMessage>(
            this, async (r, m) => await _modals.ToggleAsync(UpdateUsername, m.Value));

        WeakReferenceMessenger.Default.Register<UpdatePasswordMessage>(
            this, async (r, m) => await _modals.ToggleAsync(UpdatePassword, m.Value));
    }
}

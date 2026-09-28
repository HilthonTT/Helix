using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Users;
using Helix.App.Services;
using Helix.App.ViewModels.Settings;

namespace Helix.App.Views.Settings;

public sealed partial class SettingsPage : ContentPage
{
    private const string UpdateUsername = "update-username";
    private const string UpdatePassword = "update-password";
    private const string CreateRecoveryKey = "create-recovery-key";

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
        _modals.Register(CreateRecoveryKey, CreateRecoveryKeyLayout, CreateRecoveryKeyView);
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
            Notifier.Error(ex.Message);
        }
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<UpdateUsernameMessage>(
            this, async (r, m) => await _modals.ToggleAsync(UpdateUsername, m.Value));

        WeakReferenceMessenger.Default.Register<UpdatePasswordMessage>(
            this, async (r, m) => await _modals.ToggleAsync(UpdatePassword, m.Value));

        WeakReferenceMessenger.Default.Register<CreateRecoveryKeyMessage>(this, async (r, m) =>
        {
            if (m.Value)
            {
                CreateRecoveryKeyView.Reset();
            }

            await _modals.ToggleAsync(CreateRecoveryKey, m.Value);
        });
    }
}

using Helix.App.ViewModels.Users;
using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;

namespace Helix.App.Views.Users;

public sealed partial class LoginPage : ContentPage
{
	private IGlobalHook? _hook;

    private readonly LoginViewModel _viewModel;

    public LoginPage()
	{
		InitializeComponent();

        _viewModel = new LoginViewModel();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        SetIsLoadingToFalse();
        LoadCurrentLanguage();

        // Reuse the app-wide hook started in MauiProgram: libuiohook allows only one
        // running global hook per process, so a second hook's RunAsync faults and the
        // Ctrl+Enter shortcut would never fire. Subscribing costs nothing where the hook
        // never started - on macOS without Accessibility access, most likely - it simply
        // never raises.
        _hook = App.ServiceProvider.GetRequiredService<IGlobalHook>();
        _hook.KeyPressed += OnKeyPressed;
    }

    protected override void OnDisappearing()
    {
        if (_hook is null)
        {
            return;
        }

        _hook.KeyPressed -= OnKeyPressed;
        _hook = null;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
	{
        if (BindingContext is not LoginViewModel viewModel)
        {
            return;
        }

        if (e.Data.KeyCode != KeyCode.VcEnter || (e.RawEvent.Mask & EventMask.Ctrl) == 0)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await viewModel.LoginCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                AppLog.For<LoginPage>().LogError(ex, "The sign-in shortcut failed.");
            }
        });
    }

    private void LoadCurrentLanguage()
    {
        if (_viewModel.LoadCurrentLanguageCommand.CanExecute(null))
        {
            _viewModel.LoadCurrentLanguageCommand.Execute(null);
        }
    }

    private void SetIsLoadingToFalse()
    {
        if (_viewModel.SetLoadingToFalseCommand.CanExecute(null))
        {
            _viewModel.SetLoadingToFalseCommand.Execute(null);
        }
    }
}
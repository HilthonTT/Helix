using Helix.App.ViewModels.Users;
using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;

namespace Helix.App.Views.Users;

public sealed partial class RegisterPage : ContentPage
{
    private IGlobalHook? _hook;

    private readonly RegisterViewModel _viewModel;

    public RegisterPage()
	{
		InitializeComponent();

        _viewModel = new RegisterViewModel();

        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        SetLoadingToFalse();
        LoadCurrentLanguage();

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
        if (e.Data.KeyCode != KeyCode.VcEnter || (e.RawEvent.Mask & EventMask.Ctrl) == 0)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await _viewModel.RegisterCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                AppLog.For<RegisterPage>().LogError(ex, "The register shortcut failed.");
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

    private void SetLoadingToFalse()
    {
        if (_viewModel.SetLoadingToFalseCommand.CanExecute(null))
        {
            _viewModel.SetLoadingToFalseCommand.Execute(null);
        }
    }
}

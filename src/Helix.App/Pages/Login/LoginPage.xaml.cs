using SharpHook;
using SharpHook.Native;

namespace Helix.App.Pages.Login;

public sealed partial class LoginPage : ContentPage
{
	private TaskPoolGlobalHook? _hook;

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

        _hook = new TaskPoolGlobalHook();
        _hook.KeyPressed += OnKeyPressed;

        _ = _hook.RunAsync();
    }

    protected override void OnDisappearing()
    {
        if (_hook is null)
        {
            return;
        }

        _hook.KeyPressed -= OnKeyPressed;
        _hook.Dispose();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
	{
        if (BindingContext is not LoginViewModel viewModel)
        {
            return;
        }

        if (e.Data.KeyCode != KeyCode.VcEnter || (e.RawEvent.Mask & ModifierMask.Ctrl) == 0)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await viewModel.LoginCommand.ExecuteAsync(null);
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
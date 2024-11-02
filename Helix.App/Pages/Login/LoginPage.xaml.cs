using SharpHook;
using SharpHook.Native;

namespace Helix.App.Pages.Login;

public sealed partial class LoginPage : ContentPage
{
	private TaskPoolGlobalHook? _hook;

	public LoginPage()
	{
		InitializeComponent();

		BindingContext = new LoginViewModel();
	}

    protected override void OnAppearing()
    {
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
        if (BindingContext is not LoginViewModel viewModel)
        {
            return;
        }

        if (viewModel.LoadCurrentLanguageCommand.CanExecute(null))
        {
            viewModel.LoadCurrentLanguageCommand.Execute(null);
        }
    }
}
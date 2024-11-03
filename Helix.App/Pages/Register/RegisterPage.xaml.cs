using SharpHook;
using SharpHook.Native;

namespace Helix.App.Pages.Register;

public sealed partial class RegisterPage : ContentPage
{
    private TaskPoolGlobalHook? _hook;

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
        if (e.Data.KeyCode != KeyCode.VcEnter || (e.RawEvent.Mask & ModifierMask.Ctrl) == 0)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await _viewModel.RegisterCommand.ExecuteAsync(null);
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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Resources.Languages;
using Helix.App.Services;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Features.Users.Commands;

namespace Helix.App.ViewModels.Users;

internal sealed partial class LockViewModel : BaseViewModel
{
    private const int FreeAttempts = 3;
    private const int MaxDelaySeconds = 30;

    private static int _failures;

    private readonly ILoggedInUser _loggedInUser;

    public LockViewModel()
    {
        _loggedInUser = App.ServiceProvider.GetRequiredService<ILoggedInUser>();

        Password = string.Empty;
        Error = string.Empty;
    }

    [ObservableProperty]
    public partial string Password { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public string Username => _loggedInUser.Username;

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Error = string.Empty;

            var request = new UnlockSession.Request(Password);

            Result result = await Task.Run(() => ScopedHandler.HandleAsync((UnlockSession h) => h.Handle(request)));

            if (result.IsFailure)
            {
                Password = string.Empty;

                int failures = Interlocked.Increment(ref _failures);
                if (failures > FreeAttempts)
                {
                    int seconds = (int)Math.Min(MaxDelaySeconds, Math.Pow(2, failures - FreeAttempts));
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                }

                Error = AppResources.LockWrongPassword;

                return;
            }

            Interlocked.Exchange(ref _failures, 0);
            Password = string.Empty;

            await App.ServiceProvider.GetRequiredService<IdleLockService>().UnlockAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await App.ServiceProvider.GetRequiredService<IdleLockService>().SignOutAsync();
    }

    public void Refresh()
    {
        Error = string.Empty;
        OnPropertyChanged(nameof(Username));
    }
}

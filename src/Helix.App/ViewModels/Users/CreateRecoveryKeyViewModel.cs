using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Users;
using Helix.Application.Features.Users.Commands;

namespace Helix.App.ViewModels.Users;

internal sealed partial class CreateRecoveryKeyViewModel : BaseViewModel
{
    public CreateRecoveryKeyViewModel()
    {
        CurrentPassword = string.Empty;
        RecoveryKey = string.Empty;
    }

    [ObservableProperty]
    public partial string CurrentPassword { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingForPassword))]
    public partial bool IsShowingRecoveryKey { get; set; }

    public bool IsAskingForPassword => !IsShowingRecoveryKey;

    [ObservableProperty]
    public partial string RecoveryKey { get; set; }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new CreateRecoveryKey.Request(CurrentPassword);

            Result<string> result = await Task.Run(() => ScopedHandler.HandleAsync((CreateRecoveryKey h) => h.Handle(request)));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            CurrentPassword = string.Empty;
            RecoveryKey = result.Value;
            IsShowingRecoveryKey = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        WeakReferenceMessenger.Default.Send(new CreateRecoveryKeyMessage(false));

        Reset();
    }

    public void Reset()
    {
        CurrentPassword = string.Empty;
        RecoveryKey = string.Empty;
        IsShowingRecoveryKey = false;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Pages;
using Helix.Application.Users;
using SharedKernel;

namespace Helix.App.Modals.Users.UpdateUsername;

internal sealed partial class UpdateUsernameViewModel : BaseViewModel
{
    private readonly UpdateUser _updateUser;

    public UpdateUsernameViewModel()
    {
        _updateUser = App.ServiceProvider.GetRequiredService<UpdateUser>();

        RegisterMessages();
    }

    [ObservableProperty]
    private string _username = string.Empty;

    [RelayCommand]
    private async Task UpdateUsernameAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new UpdateUser.Request(Username);

            Result result = await _updateUser.Handle(request);
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            await DisplaySuccessAsync("You've updated your username.");

            WeakReferenceMessenger.Default.Send(new UsernameUpdatedMessage(Username));
            WeakReferenceMessenger.Default.Send(new UpdateUsernameMessage(false, string.Empty));

            Close();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        WeakReferenceMessenger.Default.Send(new UpdateUsernameMessage(false, string.Empty));
        Clear();
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<UpdateUsernameMessage>(this, (r, m) =>
        {
            Username = m.Username;
        });
    }

    private void Clear()
    {
        Username = string.Empty;
    }
}

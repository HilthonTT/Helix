using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Pages;
using Helix.Application.Users;
using SharedKernel;

namespace Helix.App.Modals.Users.UpdateUsername;

internal sealed partial class UpdateUsernameViewModel : BaseViewModel
{
    public UpdateUsernameViewModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Username = string.Empty;

        RegisterMessages();
    }

    [ObservableProperty]
    public partial string Username { get; set; }

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

            Result result = await ScopedHandler.HandleAsync((UpdateUser h) => h.Handle(request));
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

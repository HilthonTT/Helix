using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.App.ViewModels;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class CreateDriveViewModel : BaseViewModel
{
    public CreateDriveViewModel()
    {
        Form = new();
        HideSecrets = true;
        AvailableLetters = [];

        RegisterMessages();
    }

    [ObservableProperty]
    public partial CreateDriveModel Form { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> AvailableLetters { get; set; }

    [ObservableProperty]
    public partial bool HideSecrets { get; set; }

    public bool SupportsPersistentMappings => DrivePlatform.SupportsPersistentMappings;

    public bool SupportsHostnameConnect => DrivePlatform.SupportsHostnameConnect;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new CreateDrive.Request(
                Form.Letter,
                Form.Host,
                Form.Name,
                Form.Username,
                Form.Password,
                Form.AutoConnect,
                Form.Persistent,
                Form.ConnectByHostname);

            Result<Drive> result = await ScopedHandler.HandleAsync((CreateDrive h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            WeakReferenceMessenger.Default.Send(new DriveCreatedMessage(result.Value));
            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

            Close();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new TestDriveConnection.Request(
                Form.Letter,
                Form.Host,
                Form.Name,
                Form.Username,
                Form.Password);

            Result result = await ScopedHandler.HandleAsync((TestDriveConnection h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            await DisplaySuccessAsync(AppResources.ConnectionTestSucceeded);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        Form = new();
        WeakReferenceMessenger.Default.Send(new CreateDriveMessage(false));
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<CreateDriveMessage>(this, async (r, m) =>
        {
            if (!m.Value)
            {
                return;
            }

            try
            {
                await LoadAvailableLettersAsync();
            }
            catch (Exception ex)
            {
                AppLog.For<CreateDriveViewModel>().LogError(ex, "Could not load the available drive letters.");
            }
        });
    }

    private async Task LoadAvailableLettersAsync()
    {
        Result<List<string>> result = await ScopedHandler.HandleAsync(
            (GetAvailableDriveLetters h) => h.Handle());
        if (result.IsFailure)
        {
            return;
        }

        AvailableLetters = new(result.Value);

        if (string.IsNullOrEmpty(Form.Letter) && AvailableLetters.Count > 0)
        {
            Form.Letter = AvailableLetters[^1];
        }
    }
}

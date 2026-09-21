using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.App.Services;
using Helix.App.ViewModels;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class UpdateDriveViewModel : BaseViewModel
{
    public UpdateDriveViewModel()
    {
        Drive = new();
        HideSecrets = true;
        AvailableLetters = [];
        NetworkPin = new();

        RegisterMessages();
    }

    private List<Drive> _otherDrives = [];
    private string _originalUsername = string.Empty;
    private string _originalPassword = string.Empty;

    [ObservableProperty]
    public partial UpdateDriveModel Drive { get; set; }

    partial void OnDriveChanged(UpdateDriveModel oldValue, UpdateDriveModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnDrivePropertyChanged;
        }

        newValue.PropertyChanged += OnDrivePropertyChanged;

        RefreshCredentialScope();
    }

    private void OnDrivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UpdateDriveModel.Host) or
            nameof(UpdateDriveModel.Username) or
            nameof(UpdateDriveModel.Password))
        {
            RefreshCredentialScope();
        }
    }

    public NetworkPinModel NetworkPin { get; }

    [ObservableProperty]
    public partial bool ApplyCredentialsToServer { get; set; }

    public int SameServerCount => _otherDrives.Count(d => d.IsOnSameServerAs(Drive.Host ?? string.Empty));

    public bool ShowApplyCredentials =>
        SameServerCount > 0 &&
        (!string.Equals(Drive.Username, _originalUsername, StringComparison.Ordinal) ||
         !string.Equals(Drive.Password, _originalPassword, StringComparison.Ordinal));

    public string ApplyCredentialsLabel => SameServerCount == 1
        ? AppResources.ApplyCredentialsToServerOne
        : string.Format(AppResources.ApplyCredentialsToServerMany, SameServerCount);

    private void RefreshCredentialScope()
    {
        OnPropertyChanged(nameof(SameServerCount));
        OnPropertyChanged(nameof(ShowApplyCredentials));
        OnPropertyChanged(nameof(ApplyCredentialsLabel));
    }

    [ObservableProperty]
    public partial ObservableCollection<string> AvailableLetters { get; set; }

    [ObservableProperty]
    public partial bool HideSecrets { get; set; }

    public bool SupportsPersistentMappings => DrivePlatform.SupportsPersistentMappings;

    public bool SupportsHostnameConnect => DrivePlatform.SupportsHostnameConnect;

    public bool SupportsMacLookUp => App.ServiceProvider.GetRequiredService<IWakeOnLan>().CanLookUp;

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            UpdateDriveModel edited = Drive;

            var request = new UpdateDrive.Request(
                edited.Id,
                edited.Letter,
                edited.Host,
                edited.Name,
                edited.Username,
                edited.Password,
                edited.AutoConnect,
                edited.Persistent,
                edited.ConnectByHostname,
                NetworkPin.NetworkId,
                NetworkPin.NetworkName,
                edited.MacAddress,
                ApplyCredentialsToServer && ShowApplyCredentials);

            Result result = await ScopedHandler.HandleAsync((UpdateDrive h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            if (request.ApplyCredentialsToServer)
            {
                Notifier.Success(AppResources.CredentialsAppliedToServer);
            }

            var driveDisplay = new DriveDisplay(edited);
            WeakReferenceMessenger.Default.Send(new DriveUpdatedMessage(driveDisplay));

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
                Drive.Letter,
                Drive.Host,
                Drive.Name,
                Drive.Username,
                Drive.Password,
                Drive.ConnectByHostname);

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
    private async Task LookUpMacAddressAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new LookUpMacAddress.Request(Drive.Host);

            Result<string> result = await ScopedHandler.HandleAsync((LookUpMacAddress h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            Drive.MacAddress = result.Value;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void Close()
    {
        WeakReferenceMessenger.Default.Send(new UpdateDriveMessage(false, Guid.Empty));
    }

    private async Task LoadAvailableLettersAsync(Guid driveId)
    {
        var request = new GetAvailableDriveLetters.Request(driveId);

        Result<List<string>> result = await ScopedHandler.HandleAsync(
            (GetAvailableDriveLetters h) => h.Handle(request));
        if (result.IsFailure)
        {
            return;
        }

        AvailableLetters = new(result.Value);
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<UpdateDriveMessage>(this, async (r, m) =>
        {
            if (m.DriveId == Guid.Empty)
            {
                return;
            }

            IsBusy = true;
            ApplyCredentialsToServer = false;
            _otherDrives = [];
            _originalUsername = string.Empty;
            _originalPassword = string.Empty;
            Drive = new UpdateDriveModel();

            try
            {
                var request = new GetDriveById.Request(m.DriveId);

                Result<Drive> result = await ScopedHandler.HandleAsync((GetDriveById h) => h.Handle(request));
                if (result.IsFailure)
                {
                    Close();
                    return;
                }

                _originalUsername = result.Value.Username;
                _originalPassword = result.Value.Password;

                Result<List<Drive>> drives = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
                _otherDrives = drives.IsSuccess ? [.. drives.Value.Where(d => d.Id != m.DriveId)] : [];

                Drive = new UpdateDriveModel(result.Value);

                await NetworkPin.LoadAsync(result.Value.HomeNetworkId, result.Value.HomeNetworkName);

                await LoadAvailableLettersAsync(m.DriveId);
            }
            catch (Exception ex)
            {
                AppLog.For<UpdateDriveViewModel>().LogError(ex, "Could not open the drive for editing.");

                Close();
            }
            finally
            {
                IsBusy = false;
            }
        });
    }
}

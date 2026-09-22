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
using Helix.Application.Features.Drives.Contracts;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class CreateDriveViewModel : BaseViewModel
{
    public CreateDriveViewModel()
    {
        Form = new();
        HideSecrets = true;
        AvailableLetters = [];
        Shares = [];
        NetworkPin = new();

        RegisterMessages();
    }

    [ObservableProperty]
    public partial CreateDriveModel Form { get; set; }

    partial void OnFormChanged(CreateDriveModel oldValue, CreateDriveModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnFormPropertyChanged;
        }

        newValue.PropertyChanged += OnFormPropertyChanged;

        ClearShares();
    }

    private void OnFormPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CreateDriveModel.Host))
        {
            ClearShares();
        }
    }

    [ObservableProperty]
    public partial ObservableCollection<string> AvailableLetters { get; set; }

    [ObservableProperty]
    public partial bool HideSecrets { get; set; }

    public NetworkPinModel NetworkPin { get; }

    public bool SupportsPersistentMappings => DrivePlatform.SupportsPersistentMappings;

    public bool SupportsHostnameConnect => DrivePlatform.SupportsHostnameConnect;

    public bool SupportsShareBrowsing => DrivePlatform.SupportsShareBrowsing;

    public bool SupportsMacLookUp => App.ServiceProvider.GetRequiredService<IWakeOnLan>().CanLookUp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShares))]
    [NotifyPropertyChangedFor(nameof(ShowNoShares))]
    public partial ObservableCollection<ShareOption> Shares { get; set; }

    partial void OnSharesChanged(ObservableCollection<ShareOption> oldValue, ObservableCollection<ShareOption> newValue)
    {
        foreach (ShareOption share in oldValue ?? [])
        {
            share.PropertyChanged -= OnShareChanged;
        }

        foreach (ShareOption share in newValue)
        {
            share.PropertyChanged += OnShareChanged;
        }

        RefreshShareSelection();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoShares))]
    public partial bool SharesListed { get; set; }

    public bool HasShares => Shares.Count > 0;

    public bool ShowNoShares => SharesListed && !HasShares;

    public int SelectedShareCount => Shares.Count(share => share.IsSelected);

    public bool IsMultiShare => SelectedShareCount > 1;

    public bool IsSingleShare => !IsMultiShare;

    public string SharesSelectedText => string.Format(AppResources.SharesSelected, SelectedShareCount);

    private void OnShareChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShareOption.IsSelected))
        {
            return;
        }

        ShareOption[] selected = [.. Shares.Where(share => share.IsSelected)];
        if (selected.Length == 1)
        {
            Form.Name = selected[0].Name;
        }

        RefreshShareSelection();
    }

    private void RefreshShareSelection()
    {
        OnPropertyChanged(nameof(SelectedShareCount));
        OnPropertyChanged(nameof(IsMultiShare));
        OnPropertyChanged(nameof(IsSingleShare));
        OnPropertyChanged(nameof(SharesSelectedText));
    }

    private void ClearShares()
    {
        Shares = [];
        SharesListed = false;
    }

    [RelayCommand]
    private async Task BrowseSharesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new ListShares.Request(Form.Host, Form.Username, Form.Password);

            Result<List<AvailableShare>> result = await ScopedHandler.HandleAsync((ListShares h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            ObservableCollection<ShareOption> shares = [.. result.Value.Select(share => new ShareOption(share))];

            ShareOption? current = shares.FirstOrDefault(share =>
                share.CanSelect && string.Equals(share.Name, Form.Name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (current is not null)
            {
                current.IsSelected = true;
            }

            Shares = shares;
            SharesListed = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

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

            if (IsMultiShare)
            {
                await SaveSelectedSharesAsync();
                return;
            }

            var request = new CreateDrive.Request(
                Form.Letter,
                Form.Host,
                Form.Name,
                Form.Username,
                Form.Password,
                Form.AutoConnect,
                Form.Persistent,
                Form.ConnectByHostname,
                NetworkPin.NetworkId,
                NetworkPin.NetworkName,
                Form.MacAddress,
                Form.RemoteHost);

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

    private async Task SaveSelectedSharesAsync()
    {
        string[] names = [.. Shares.Where(share => share.IsSelected).Select(share => share.Name)];

        List<string> letters = LettersFor(names.Length);
        if (letters.Count < names.Length)
        {
            Notifier.Error(AppResources.NotEnoughLetters);
            return;
        }

        var request = new CreateDrives.Request(
            [.. names.Select((name, i) => new CreateDrives.NewDrive(letters[i], Form.Host, name))],
            Form.Username,
            Form.Password,
            Form.AutoConnect,
            Form.Persistent,
            Form.ConnectByHostname,
            NetworkPin.NetworkId,
            NetworkPin.NetworkName,
            Form.MacAddress,
            Form.RemoteHost);

        Result<List<Drive>> result = await ScopedHandler.HandleAsync((CreateDrives h) => h.Handle(request));
        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        foreach (Drive drive in result.Value)
        {
            WeakReferenceMessenger.Default.Send(new DriveCreatedMessage(drive));
        }

        WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

        await DisplaySuccessAsync(result.Value.Count == 1
                ? AppResources.DriveAddedOne
                : string.Format(AppResources.DrivesAdded, result.Value.Count));

        Close();
    }

    private List<string> LettersFor(int count)
    {
        List<string> letters = [];

        if (!string.IsNullOrEmpty(Form.Letter))
        {
            letters.Add(Form.Letter);
        }

        letters.AddRange(AvailableLetters
            .Reverse()
            .Where(letter => !string.Equals(letter, Form.Letter, StringComparison.OrdinalIgnoreCase)));

        return [.. letters.Take(count)];
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
                Form.Password,
                Form.ConnectByHostname,
                Form.RemoteHost);

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

            var request = new LookUpMacAddress.Request(Form.Host);

            Result<string> result = await ScopedHandler.HandleAsync((LookUpMacAddress h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            Form.MacAddress = result.Value;
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
                IsBusy = true;

                Drive? template = m.TemplateDriveId is Guid templateId
                    ? await LoadTemplateAsync(templateId)
                    : null;

                Form = template is null ? new() : CreateDriveModel.CopyOf(template);

                await NetworkPin.LoadAsync(template?.HomeNetworkId, template?.HomeNetworkName);

                await LoadAvailableLettersAsync();
            }
            catch (Exception ex)
            {
                AppLog.For<CreateDriveViewModel>().LogError(ex, "Could not prepare the add-drive sheet.");
            }
            finally
            {
                IsBusy = false;
            }
        });
    }

    private static async Task<Drive?> LoadTemplateAsync(Guid driveId)
    {
        var request = new GetDriveById.Request(driveId);

        Result<Drive> result = await ScopedHandler.HandleAsync((GetDriveById h) => h.Handle(request));

        return result.IsSuccess ? result.Value : null;
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

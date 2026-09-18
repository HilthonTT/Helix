using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.App.ViewModels;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class AdoptMappingsViewModel : BaseViewModel
{
    public AdoptMappingsViewModel()
    {
        Mappings = [];
        Username = string.Empty;
        Password = string.Empty;
        HideSecrets = true;

        RegisterMessages();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMappings))]
    [NotifyPropertyChangedFor(nameof(ShowNoMappings))]
    public partial ObservableCollection<MappingOption> Mappings { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoMappings))]
    public partial bool Loaded { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial bool HideSecrets { get; set; }

    public bool HasMappings => Mappings.Count > 0;

    public bool ShowNoMappings => Loaded && !HasMappings;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        MappedShare[] chosen = [.. Mappings.Where(m => m.IsSelected).Select(m => m.Mapping)];

        try
        {
            IsBusy = true;

            var request = new CreateDrives.Request(
                [.. chosen.Select(m => new CreateDrives.NewDrive(m.Letter, m.Host, m.Share))],
                Username,
                Password,
                AutoConnect: true,
                Persistent: DrivePlatform.SupportsPersistentMappings);

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
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void Close()
    {
        WeakReferenceMessenger.Default.Send(new AdoptMappingsMessage(false));
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<AdoptMappingsMessage>(this, async (r, m) =>
        {
            if (!m.Value)
            {
                return;
            }

            Mappings = [];
            Loaded = false;
            Username = string.Empty;
            Password = string.Empty;
            HideSecrets = true;

            try
            {
                IsBusy = true;

                Result<List<MappedShare>> result = await ScopedHandler.HandleAsync(
                    (GetUnmanagedMappings h) => h.Handle());
                if (result.IsFailure)
                {
                    await DisplayErrorAsync(result.Error);
                    return;
                }

                Mappings = [.. result.Value.Select(mapping => new MappingOption(mapping))];
                Loaded = true;
            }
            catch (Exception ex)
            {
                AppLog.For<AdoptMappingsViewModel>().LogError(ex, "Could not read the existing drive mappings.");
            }
            finally
            {
                IsBusy = false;
            }
        });
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.DriveGroups;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.Application.Features.DriveGroups.Commands;
using Helix.Application.Features.DriveGroups.Queries;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class DriveGroupsViewModel : BaseViewModel
{
    private List<Drive> _drives = [];

    public DriveGroupsViewModel()
    {
        Groups = [];
        Drives = [];
        Name = string.Empty;

        RegisterMessages();
    }

    [ObservableProperty]
    public partial ObservableCollection<DriveGroupDisplay> Groups { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<DriveSelection> Drives { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    public partial DriveGroupDisplay? Editing { get; set; }

    public bool IsEditing => Editing is not null;

    public string EditorTitle => Editing is null
        ? AppResources.NewGroup
        : Editing.Name;

    public bool HasDrives => Drives.Count > 0;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            Result<List<Drive>> drivesResult = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
            if (drivesResult.IsFailure)
            {
                await DisplayErrorAsync(drivesResult.Error);
                return;
            }

            _drives = drivesResult.Value;

            await ReloadGroupsAsync();

            StartNewGroup();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartNewGroup()
    {
        Editing = null;
        Name = string.Empty;

        SetSelection([]);
    }

    [RelayCommand]
    private void Edit(DriveGroupDisplay? group)
    {
        if (group is null)
        {
            return;
        }

        Editing = group;
        Name = group.Name;

        SetSelection(group.DriveIds);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        List<Guid> selected = [.. Drives.Where(d => d.IsSelected).Select(d => d.Id)];

        try
        {
            IsBusy = true;

            Result result = Editing is null
                ? await ScopedHandler.HandleAsync((CreateDriveGroup h) =>
                    h.Handle(new CreateDriveGroup.Request(Name, selected)))
                : await ScopedHandler.HandleAsync((UpdateDriveGroup h) =>
                    h.Handle(new UpdateDriveGroup.Request(Editing.Id, Name, selected)));

            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            await ReloadGroupsAsync();

            StartNewGroup();

            WeakReferenceMessenger.Default.Send(new DriveGroupsChangedMessage());
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(DriveGroupDisplay? group)
    {
        if (group is null || IsBusy)
        {
            return;
        }

        bool confirmed = await Shell.Current.DisplayAlertAsync(
            AppResources.DeleteGroup,
            string.Format(AppResources.DeleteGroupConfirm, group.Name),
            AppResources.Delete,
            AppResources.Cancel);

        if (!confirmed)
        {
            return;
        }

        try
        {
            IsBusy = true;

            Result result = await ScopedHandler.HandleAsync((DeleteDriveGroup h) =>
                h.Handle(new DeleteDriveGroup.Request(group.Id)));

            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            await ReloadGroupsAsync();

            if (Editing?.Id == group.Id)
            {
                StartNewGroup();
            }

            WeakReferenceMessenger.Default.Send(new DriveGroupsChangedMessage());
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void Close()
    {
        WeakReferenceMessenger.Default.Send(new DriveGroupsMessage(false));
    }

    private async Task ReloadGroupsAsync()
    {
        Result<List<DriveGroup>> result = await ScopedHandler.HandleAsync((GetDriveGroups h) => h.Handle());
        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        HashSet<Guid> existing = [.. _drives.Select(drive => drive.Id)];

        Groups = [.. result.Value.Select(group => new DriveGroupDisplay(group, existing))];
    }

    private void SetSelection(IReadOnlyList<Guid> selectedIds)
    {
        HashSet<Guid> selected = [.. selectedIds];

        Drives = [.. _drives.Select(drive => new DriveSelection(drive, selected.Contains(drive.Id)))];

        OnPropertyChanged(nameof(HasDrives));
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<DriveGroupsMessage>(this, (r, m) =>
        {
            if (m.Show)
            {
                _ = LoadAsync();
            }
        });
    }
}

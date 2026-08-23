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

/// <summary>
/// The group manager: the list of groups on one side of the sheet, and the group being
/// edited on the other.
/// </summary>
/// <remarks>
/// One sheet for create, rename, re-member and delete rather than four. A group is a name
/// and a set of ticks — there is not enough in it to justify its own modal per verb, and
/// the thing users actually do is open it, adjust two groups and close it again.
/// </remarks>
internal sealed partial class DriveGroupsViewModel : BaseViewModel
{
    /// <summary>The user's drives, as the last load saw them.</summary>
    private List<Drive> _drives = [];

    public DriveGroupsViewModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Groups = [];
        Drives = [];
        Name = string.Empty;

        RegisterMessages();
    }

    [ObservableProperty]
    public partial ObservableCollection<DriveGroupDisplay> Groups { get; set; }

    /// <summary>Every drive the user has, ticked where it is in the group being edited.</summary>
    [ObservableProperty]
    public partial ObservableCollection<DriveSelection> Drives { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>
    /// The group being edited, or null while a new one is being composed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    public partial DriveGroupDisplay? Editing { get; set; }

    public bool IsEditing => Editing is not null;

    public string EditorTitle => Editing is null
        ? AppResources.NewGroup
        : Editing.Name;

    /// <summary>Whether the user has any drives to put in a group at all.</summary>
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

            // Opening the sheet always starts on a blank group: the common reason to open
            // it is to make one, and an editor pre-loaded with an existing group invites
            // editing the wrong one.
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

            // The editor may have been showing the group that just went.
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

using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Drives;

namespace Helix.App.Models;

/// <summary>One drive in the group editor's checklist.</summary>
internal sealed partial class DriveSelection : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public Guid Id { get; }

    /// <summary>"Z: — Media Vault", so the list reads the way the drive list does.</summary>
    public string Label { get; }

    public DriveSelection(Drive drive, bool isSelected)
    {
        Id = drive.Id;
        Label = $"{drive.Letter}: — {drive.Name}";
        IsSelected = isSelected;
    }
}

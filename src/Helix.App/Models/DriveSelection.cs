using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Drives;

namespace Helix.App.Models;

internal sealed partial class DriveSelection : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public Guid Id { get; }

    public string Label { get; }

    public DriveSelection(Drive drive, bool isSelected)
    {
        Id = drive.Id;
        Label = $"{drive.Letter}: — {drive.Name}";
        IsSelected = isSelected;
    }
}

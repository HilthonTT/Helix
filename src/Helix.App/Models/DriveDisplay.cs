using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Drives;

namespace Helix.App.Models;

internal sealed partial class DriveDisplay : ObservableObject
{
    [ObservableProperty]
    public partial Guid Id { get; set; }

    [ObservableProperty]
    public partial string Letter { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Human-readable capacity line, refreshed whenever connectivity changes.</summary>
    [ObservableProperty]
    public partial string StorageUsage { get; set; }

    /// <summary>Drives the status pill in the drive row; bound, so the UI follows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Disconnected))]
    public partial bool Connected { get; set; }

    public bool Disconnected => !Connected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    public DriveDisplay(Drive drive)
        : this()
    {
        Id = drive.Id;
        Letter = drive.Letter;
        Name = drive.Name;
    }

    public DriveDisplay(UpdateDriveModel updateDrive)
        : this()
    {
        Id = updateDrive.Id;
        Letter = updateDrive.Letter;
        Name = updateDrive.Name;
    }

    public DriveDisplay()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        // The other constructors chain through this one to pick them up.
        Id = Guid.Empty;
        Letter = string.Empty;
        Name = string.Empty;
        StorageUsage = string.Empty;
    }
}

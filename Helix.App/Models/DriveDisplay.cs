using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Drives;

namespace Helix.App.Models;

internal sealed partial class DriveDisplay : ObservableObject
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private string _letter = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    public string _buttonColor = "#ff0000";

    [ObservableProperty]
    private bool _connected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy = false;

    public bool IsNotBusy => !IsBusy;

    public DriveDisplay(Drive drive)
    {
        Id = drive.Id;
        Letter = drive.Letter;
        Name = drive.Name;
        ButtonColor = "#ff0000";
    }

    public DriveDisplay()
    {
        Id = Guid.Empty;
        Letter = string.Empty;
        Name = string.Empty;
    }
}

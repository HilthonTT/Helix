using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Drives;

namespace Helix.App.Models;

internal sealed partial class DriveDisplay : ObservableObject
{
    [ObservableProperty]
    private string _letter = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActualButtonColor))]
    private string _buttonColor = Colors.Red.ToString();

    private Color ActualButtonColor => Color.FromArgb(ButtonColor);

    [ObservableProperty]
    private bool _connected;

    public DriveDisplay(Drive drive)
    {
        Letter = drive.Letter;
        Name = drive.Name;
    }
}

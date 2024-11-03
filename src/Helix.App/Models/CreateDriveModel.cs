using CommunityToolkit.Mvvm.ComponentModel;

namespace Helix.App.Models;

internal sealed partial class CreateDriveModel : ObservableObject
{
    [ObservableProperty]
    private string _letter = string.Empty;

    [ObservableProperty]
    private string _ipAddress = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace Helix.App.Models;

internal sealed partial class CreateDriveModel : ObservableObject
{
    public CreateDriveModel()
    {
        Letter = string.Empty;
        Host = string.Empty;
        Name = string.Empty;
        Username = string.Empty;
        Password = string.Empty;

        AutoConnect = true;
        Persistent = false;
        ConnectByHostname = false;
    }

    [ObservableProperty]
    public partial string Letter { get; set; }

    [ObservableProperty]
    public partial string Host { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial bool AutoConnect { get; set; }

    [ObservableProperty]
    public partial bool Persistent { get; set; }

    [ObservableProperty]
    public partial bool ConnectByHostname { get; set; }
}

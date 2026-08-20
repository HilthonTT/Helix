using CommunityToolkit.Mvvm.ComponentModel;

namespace Helix.App.Models;

internal sealed partial class CreateDriveModel : ObservableObject
{
    public CreateDriveModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Letter = string.Empty;
        Host = string.Empty;
        Name = string.Empty;
        Username = string.Empty;
        Password = string.Empty;

        // Matches the handler's own default: a drive added without touching the switch
        // takes part in the automatic passes, the way every drive did before the flag.
        AutoConnect = true;
        Persistent = false;
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
}

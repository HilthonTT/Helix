using CommunityToolkit.Mvvm.ComponentModel;

namespace Helix.App.Models;

internal sealed partial class CreateDriveModel : ObservableObject
{
    public CreateDriveModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Letter = string.Empty;
        IpAddress = string.Empty;
        Name = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
    }

    [ObservableProperty]
    public partial string Letter { get; set; }

    [ObservableProperty]
    public partial string IpAddress { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }
}

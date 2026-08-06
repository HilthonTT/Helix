using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Drives;

namespace Helix.App.Models;

internal sealed partial class UpdateDriveModel : ObservableObject
{
    [ObservableProperty]
    public partial Guid Id { get; set; }

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

    public UpdateDriveModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Letter = string.Empty;
        IpAddress = string.Empty;
        Name = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
    }

    public UpdateDriveModel(Drive drive)
        : this()
    {
        Id = drive.Id;
        Letter = drive.Letter;
        IpAddress = drive.IpAddress;
        Name = drive.Name;
        Username = drive.Username;
        Password = drive.Password;
    }
}

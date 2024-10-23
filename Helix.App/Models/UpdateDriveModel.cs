using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Drives;

namespace Helix.App.Models;

internal sealed partial class UpdateDriveModel : ObservableObject
{
    [ObservableProperty]
    private Guid _id;

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

    public UpdateDriveModel()
    {
    }

    public UpdateDriveModel(Drive drive)
    {
        Id = drive.Id;
        Letter = drive.Letter;
        IpAddress = drive.IpAddress;
        Name = drive.Name;
        Username = drive.Username;
        Password = drive.Password;
    }
}

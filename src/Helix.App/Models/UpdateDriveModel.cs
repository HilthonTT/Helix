using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Drives;
using System.Globalization;

namespace Helix.App.Models;

internal sealed partial class UpdateDriveModel : ObservableObject
{
    [ObservableProperty]
    public partial Guid Id { get; set; }

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

    [ObservableProperty]
    public partial string MacAddress { get; set; }

    [ObservableProperty]
    public partial string RemoteHost { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Protocol))]
    [NotifyPropertyChangedFor(nameof(IsSmb))]
    [NotifyPropertyChangedFor(nameof(IsWebDav))]
    [NotifyPropertyChangedFor(nameof(UsesCredentials))]
    [NotifyPropertyChangedFor(nameof(PathHint))]
    public partial int ProtocolIndex { get; set; }

    [ObservableProperty]
    public partial string Port { get; set; }

    public DriveProtocol Protocol => DriveProtocolText.FromIndex(ProtocolIndex);

    public bool IsSmb => Protocol == DriveProtocol.Smb;

    public bool IsWebDav => Protocol == DriveProtocol.WebDav;

    public bool UsesCredentials => Protocol.UsesCredentials();

    public string PathHint => DriveProtocolText.PathHintFor(Protocol);

    public int? PortNumber => DriveProtocolText.ParsePort(Port);

    public UpdateDriveModel()
    {
        Letter = string.Empty;
        Host = string.Empty;
        Name = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        AutoConnect = true;
        Persistent = false;
        ConnectByHostname = false;
        MacAddress = string.Empty;
        RemoteHost = string.Empty;
        Port = string.Empty;
    }

    public UpdateDriveModel(Drive drive)
        : this()
    {
        Id = drive.Id;
        Letter = drive.Letter;
        Host = drive.Host;
        Name = drive.Name;
        Username = drive.Username;
        Password = drive.Password;
        AutoConnect = drive.AutoConnect;
        Persistent = drive.Persistent;
        ConnectByHostname = drive.ConnectByHostname;
        MacAddress = drive.MacAddress ?? string.Empty;
        RemoteHost = drive.RemoteHost ?? string.Empty;
        ProtocolIndex = (int)drive.Protocol;
        Port = drive.Port?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }
}

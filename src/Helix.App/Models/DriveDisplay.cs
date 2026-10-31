using CommunityToolkit.Mvvm.ComponentModel;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Connector;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string Host { get; set; }

    // Not shown; it is what lets IsUp recognise a mount made through the away address.
    public string? RemoteHost { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial DriveProtocol Protocol { get; set; }

    public int? Port { get; set; }

    private string HostLabel => Protocol == DriveProtocol.Smb
        ? Host
        : $"{Host} · {DriveProtocolText.ShortName(Protocol)}";

    [ObservableProperty]
    public partial string StorageUsage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastConnected))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial DateTime? LastConnectedOnUtc { get; set; }

    public string LastConnected => LastConnectedOnUtc is null
        ? AppResources.NeverConnected
        : string.Format(
            AppResources.LastConnectedAt,
            LastConnectedOnUtc.Value.ToLocalTime().ToString("g"));

    public string Subtitle => string.IsNullOrWhiteSpace(Host)
        ? LastConnected
        : $"{HostLabel} · {LastConnected}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool CanWake { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnected))]
    [NotifyPropertyChangedFor(nameof(ShowDisconnected))]
    [NotifyPropertyChangedFor(nameof(ShowUnreachable))]
    [NotifyPropertyChangedFor(nameof(ShowFailed))]
    [NotifyPropertyChangedFor(nameof(ShowAway))]
    [NotifyPropertyChangedFor(nameof(ToggleConnectText))]
    public partial bool Connected { get; set; }

    public string ToggleConnectText => Connected ? AppResources.Disconnect : AppResources.Connect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(ShowConnected))]
    [NotifyPropertyChangedFor(nameof(ShowDisconnected))]
    [NotifyPropertyChangedFor(nameof(ShowUnreachable))]
    [NotifyPropertyChangedFor(nameof(ShowFailed))]
    [NotifyPropertyChangedFor(nameof(ShowAway))]
    [NotifyPropertyChangedFor(nameof(BusyText))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDisconnected))]
    [NotifyPropertyChangedFor(nameof(ShowUnreachable))]
    [NotifyPropertyChangedFor(nameof(ShowFailed))]
    [NotifyPropertyChangedFor(nameof(ShowAway))]
    public partial DriveOfflineReason OfflineReason { get; set; }

    [ObservableProperty]
    public partial string OfflineDetail { get; set; }

    public bool IsNotBusy => !IsBusy;

    public bool ShowConnected => Connected && !IsBusy;

    public bool ShowDisconnected => IsOffline && OfflineReason == DriveOfflineReason.Unknown;

    public bool ShowUnreachable => IsOffline && OfflineReason == DriveOfflineReason.HostUnreachable;

    public bool ShowFailed => IsOffline && OfflineReason == DriveOfflineReason.Refused;

    public bool ShowAway => IsOffline && OfflineReason == DriveOfflineReason.AwayFromHome;

    private bool IsOffline => !Connected && !IsBusy;

    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(LastConnected));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ToggleConnectText));

        if (OfflineReason == DriveOfflineReason.HostUnreachable)
        {
            OfflineDetail = AppResources.StatusUnreachableHint;
        }
        else if (OfflineReason == DriveOfflineReason.AwayFromHome)
        {
            OfflineDetail = AppResources.StatusAwayHint;
        }
    }

    public void MarkOffline(DriveOfflineReason reason, string detail)
    {
        OfflineReason = reason;
        OfflineDetail = detail;
    }

    public void MarkOffline(Error error)
    {
        if (Connected)
        {
            return;
        }

        if (error.Code == DriveErrors.HostUnreachableCode)
        {
            MarkOffline(DriveOfflineReason.HostUnreachable, AppResources.StatusUnreachableHint);

            return;
        }

        if (error.Code == DriveErrors.AwayFromHomeNetworkCode)
        {
            MarkOffline(DriveOfflineReason.AwayFromHome, AppResources.StatusAwayHint);

            return;
        }

        MarkOffline(DriveOfflineReason.Refused, error.Description);
    }

    partial void OnConnectedChanged(bool value)
    {
        if (value)
        {
            MarkOffline(DriveOfflineReason.Unknown, string.Empty);
        }
    }

    public string BusyText => _busyText;

    private string _busyText = string.Empty;

    partial void OnIsBusyChanging(bool value)
    {
        if (value)
        {
            _busyText = Connected ? AppResources.Disconnecting : AppResources.Connecting;
        }
    }

    public DriveDisplay(Drive drive)
        : this()
    {
        Id = drive.Id;
        Letter = drive.Letter;
        Name = drive.Name;
        Host = drive.Host;
        RemoteHost = drive.RemoteHost;
        Protocol = drive.Protocol;
        Port = drive.Port;
        LastConnectedOnUtc = drive.LastConnectedOnUtc;
        CanWake = drive.MacAddress is not null;
    }

    public DriveDisplay(UpdateDriveModel updateDrive)
        : this()
    {
        Id = updateDrive.Id;
        Letter = updateDrive.Letter;
        Name = updateDrive.Name;
        Host = updateDrive.Host;
        RemoteHost = string.IsNullOrWhiteSpace(updateDrive.RemoteHost) ? null : updateDrive.RemoteHost.Trim();
        Protocol = updateDrive.Protocol;
        Port = DriveProtocols.PortFor(updateDrive.Protocol, updateDrive.PortNumber);
        CanWake = MacAddresses.IsValid(updateDrive.MacAddress);
    }

    /// <summary>
    /// Whether this drive is up from its own share. A letter that is merely present — a USB stick
    /// on it, or a remembered mapping whose restore failed — is not the drive being connected.
    /// </summary>
    public bool IsUp(INasConnector nasConnector, HashSet<string> liveLetters) =>
        CanProbe && liveLetters.Contains(Letter) && nasConnector.IsLiveFrom(Probe());

    /// <summary>
    /// Whether this drive's letter points at its own share, remembered or live — what a
    /// disconnect would take down.
    /// </summary>
    public bool IsMapped(INasConnector nasConnector, HashSet<string> presentLetters) =>
        CanProbe && presentLetters.Contains(Letter) && nasConnector.IsMountedFrom(Probe());

    // What Drive's constructor insists on; a row missing any of it cannot be anybody's share.
    private bool CanProbe =>
        Letter?.Trim().Length == 1 && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Name);

    // A Drive refuses an empty user id; the probe is never saved, so any id will do.
    private static readonly Guid ProbeOwner = Guid.NewGuid();

    private Drive Probe()
    {
        var probe = Drive.Create(ProbeOwner, Letter, Host, Name, "-", "-", protocol: Protocol, port: Port);
        probe.ReachAwayAt(RemoteHost);

        return probe;
    }

    public DriveDisplay()
    {
        Id = Guid.Empty;
        Letter = string.Empty;
        Name = string.Empty;
        Host = string.Empty;
        StorageUsage = string.Empty;
        OfflineDetail = string.Empty;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using Helix.App.Resources.Languages;
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
        : $"{Host} · {LastConnected}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnected))]
    [NotifyPropertyChangedFor(nameof(ShowDisconnected))]
    [NotifyPropertyChangedFor(nameof(ShowUnreachable))]
    [NotifyPropertyChangedFor(nameof(ShowFailed))]
    public partial bool Connected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(ShowConnected))]
    [NotifyPropertyChangedFor(nameof(ShowDisconnected))]
    [NotifyPropertyChangedFor(nameof(ShowUnreachable))]
    [NotifyPropertyChangedFor(nameof(ShowFailed))]
    [NotifyPropertyChangedFor(nameof(BusyText))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDisconnected))]
    [NotifyPropertyChangedFor(nameof(ShowUnreachable))]
    [NotifyPropertyChangedFor(nameof(ShowFailed))]
    public partial DriveOfflineReason OfflineReason { get; set; }

    [ObservableProperty]
    public partial string OfflineDetail { get; set; }

    public bool IsNotBusy => !IsBusy;

    public bool ShowConnected => Connected && !IsBusy;

    public bool ShowDisconnected => IsOffline && OfflineReason == DriveOfflineReason.Unknown;

    public bool ShowUnreachable => IsOffline && OfflineReason == DriveOfflineReason.HostUnreachable;

    public bool ShowFailed => IsOffline && OfflineReason == DriveOfflineReason.Refused;

    private bool IsOffline => !Connected && !IsBusy;

    public void MarkOffline(DriveOfflineReason reason, string detail)
    {
        OfflineReason = reason;
        OfflineDetail = detail;
    }

    public void MarkOffline(Error error)
    {
        if (error.Code == DriveErrors.HostUnreachableCode)
        {
            MarkOffline(DriveOfflineReason.HostUnreachable, AppResources.StatusUnreachableHint);

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
        LastConnectedOnUtc = drive.LastConnectedOnUtc;
    }

    public DriveDisplay(UpdateDriveModel updateDrive)
        : this()
    {
        Id = updateDrive.Id;
        Letter = updateDrive.Letter;
        Name = updateDrive.Name;
        Host = updateDrive.Host;
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

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

    /// <summary>
    /// The NAS this drive is on, as the user typed it.
    /// </summary>
    /// <remarks>
    /// On the row because a name is whatever the user called it: two drives on two
    /// different NASes were told apart only by that, and the address is the thing they
    /// actually differ by. Shown beside the last-connected stamp, not in place of it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string Host { get; set; }

    /// <summary>Human-readable capacity line, refreshed whenever connectivity changes.</summary>
    [ObservableProperty]
    public partial string StorageUsage { get; set; }

    /// <summary>
    /// When the drive last connected, or null if it never has.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastConnected))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial DateTime? LastConnectedOnUtc { get; set; }

    /// <summary>
    /// The stamp as the row shows it, in local time.
    /// </summary>
    /// <remarks>
    /// "Offline" on its own says nothing about whether a drive dropped a minute ago or
    /// has not been reachable since it was added. Shown in local time because that is
    /// the clock the user was looking at when it happened; the audit log keeps UTC.
    /// </remarks>
    public string LastConnected => LastConnectedOnUtc is null
        ? AppResources.NeverConnected
        : string.Format(
            AppResources.LastConnectedAt,
            LastConnectedOnUtc.Value.ToLocalTime().ToString("g"));

    /// <summary>The row's second line: which NAS, and when it was last up.</summary>
    public string Subtitle => string.IsNullOrWhiteSpace(Host)
        ? LastConnected
        : $"{Host} · {LastConnected}";

    /// <summary>
    /// Whether the row is ticked for a bulk action.
    /// </summary>
    /// <remarks>
    /// Selection is deliberately not persisted and not part of the drive: it lasts as long
    /// as the user is looking at the list, and replacing the collection — a search, a
    /// reload — clears it, because the rows it referred to are gone.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Drives the status pill in the drive row; bound, so the UI follows it.</summary>
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

    /// <summary>
    /// Why the drive is not mounted, when an attempt has been made and can say.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDisconnected))]
    [NotifyPropertyChangedFor(nameof(ShowUnreachable))]
    [NotifyPropertyChangedFor(nameof(ShowFailed))]
    public partial DriveOfflineReason OfflineReason { get; set; }

    /// <summary>
    /// The tooltip on the offline pill: what went wrong, in full.
    /// </summary>
    /// <remarks>
    /// The pill itself is one word because it lives in a 160px column beside twelve
    /// others. The sentence that makes it actionable — which host did not answer, or what
    /// the share said — hangs off it rather than being lost.
    /// </remarks>
    [ObservableProperty]
    public partial string OfflineDetail { get; set; }

    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// The status pill's states are mutually exclusive, and the busy one wins: a mount in
    /// flight is neither connected nor disconnected yet, and leaving the old pill up while
    /// it ran is what made the row look unresponsive.
    /// </summary>
    public bool ShowConnected => Connected && !IsBusy;

    /// <summary>
    /// Not mounted, with nothing to add. A drive the user disconnected is in this state,
    /// which is the point: it is the resting pill, not the failure one.
    /// </summary>
    public bool ShowDisconnected => IsOffline && OfflineReason == DriveOfflineReason.Unknown;

    /// <summary>The NAS is not answering — a wait rather than a failure.</summary>
    public bool ShowUnreachable => IsOffline && OfflineReason == DriveOfflineReason.HostUnreachable;

    /// <summary>The NAS answered and the mount was refused.</summary>
    public bool ShowFailed => IsOffline && OfflineReason == DriveOfflineReason.Refused;

    private bool IsOffline => !Connected && !IsBusy;

    /// <summary>
    /// Records why an attempt did not mount the drive, so the row can say which kind of
    /// "not connected" this is.
    /// </summary>
    public void MarkOffline(DriveOfflineReason reason, string detail)
    {
        OfflineReason = reason;
        OfflineDetail = detail;
    }

    /// <summary>
    /// The one mapping from a failed attempt's error to what the pill says: the
    /// unreachable code gets the localized sentence, anything else the share's own words,
    /// since the domain's descriptions are not translated.
    /// </summary>
    public void MarkOffline(Error error)
    {
        if (error.Code == DriveErrors.HostUnreachableCode)
        {
            MarkOffline(DriveOfflineReason.HostUnreachable, AppResources.StatusUnreachableHint);

            return;
        }

        MarkOffline(DriveOfflineReason.Refused, error.Description);
    }

    /// <summary>
    /// A drive that came up has no reason to be offline any more, whoever mounted it. Left
    /// behind, a stale reason would resurface as soon as the drive next dropped and blame
    /// the new outage on the old one.
    /// </summary>
    partial void OnConnectedChanged(bool value)
    {
        if (value)
        {
            MarkOffline(DriveOfflineReason.Unknown, string.Empty);
        }
    }

    /// <summary>What the busy pill says while an operation is in flight.</summary>
    /// <remarks>
    /// Snapshotted when the row goes busy rather than read off <see cref="Connected"/>
    /// each time: the handler flips Connected while IsBusy is still held, so a live
    /// read would flash "Disconnecting" at the end of a successful connect.
    /// </remarks>
    public string BusyText => _busyText;

    private string _busyText = string.Empty;

    partial void OnIsBusyChanging(bool value)
    {
        if (value)
        {
            // The row only ever toggles, so the operation starting is the opposite of
            // the state it is in.
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
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        // The other constructors chain through this one to pick them up.
        Id = Guid.Empty;
        Letter = string.Empty;
        Name = string.Empty;
        Host = string.Empty;
        StorageUsage = string.Empty;
        OfflineDetail = string.Empty;
    }
}

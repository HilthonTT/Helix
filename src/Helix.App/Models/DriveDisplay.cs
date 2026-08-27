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

    /// <summary>Human-readable capacity line, refreshed whenever connectivity changes.</summary>
    [ObservableProperty]
    public partial string StorageUsage { get; set; }

    /// <summary>
    /// When the drive last connected, or null if it never has.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastConnected))]
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

    /// <summary>Drives the status pill in the drive row; bound, so the UI follows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnected))]
    [NotifyPropertyChangedFor(nameof(ShowDisconnected))]
    public partial bool Connected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(ShowConnected))]
    [NotifyPropertyChangedFor(nameof(ShowDisconnected))]
    [NotifyPropertyChangedFor(nameof(BusyText))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// The three states the status pill can be in are mutually exclusive, and the busy
    /// one wins: a mount in flight is neither connected nor disconnected yet, and
    /// leaving the old pill up while it ran is what made the row look unresponsive.
    /// </summary>
    public bool ShowConnected => Connected && !IsBusy;

    public bool ShowDisconnected => !Connected && !IsBusy;

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
        LastConnectedOnUtc = drive.LastConnectedOnUtc;
    }

    public DriveDisplay(UpdateDriveModel updateDrive)
        : this()
    {
        Id = updateDrive.Id;
        Letter = updateDrive.Letter;
        Name = updateDrive.Name;
    }

    public DriveDisplay()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        // The other constructors chain through this one to pick them up.
        Id = Guid.Empty;
        Letter = string.Empty;
        Name = string.Empty;
        StorageUsage = string.Empty;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.DriveGroups;

namespace Helix.App.Models;

/// <summary>What the dashboard's group strip and the group manager bind to.</summary>
internal sealed partial class DriveGroupDisplay : ObservableObject
{
    [ObservableProperty]
    public partial Guid Id { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>
    /// The drives the group names, as the group stores them — including any whose drive
    /// has since been deleted.
    /// </summary>
    /// <remarks>
    /// Kept whole rather than filtered here so the editor can round-trip a group without
    /// quietly dropping members; <see cref="MemberCount"/> is what the strip counts, and
    /// it counts what actually exists.
    /// </remarks>
    public IReadOnlyList<Guid> DriveIds { get; set; } = [];

    /// <summary>How many of the group's drives still exist.</summary>
    [ObservableProperty]
    public partial int MemberCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    public DriveGroupDisplay()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Id = Guid.Empty;
        Name = string.Empty;
    }

    public DriveGroupDisplay(DriveGroup group, IReadOnlyCollection<Guid> existingDriveIds)
        : this()
    {
        Id = group.Id;
        Name = group.Name;
        DriveIds = group.DriveIds;
        MemberCount = group.DriveIds.Count(existingDriveIds.Contains);
    }
}

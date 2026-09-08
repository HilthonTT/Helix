using CommunityToolkit.Mvvm.ComponentModel;
using Helix.App.Resources.Languages;
using Helix.Domain.DriveGroups;

namespace Helix.App.Models;

internal sealed partial class DriveGroupDisplay : ObservableObject
{
    [ObservableProperty]
    public partial Guid Id { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    public IReadOnlyList<Guid> DriveIds { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemberCountText))]
    public partial int MemberCount { get; set; }

    public string MemberCountText => string.Format(AppResources.GroupMemberCount, MemberCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    public DriveGroupDisplay()
    {
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

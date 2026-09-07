using CommunityToolkit.Mvvm.ComponentModel;

namespace Helix.App.Common;

internal sealed partial class DriveListLayout : ObservableObject
{
    public static DriveListLayout Instance { get; } = new();

    private DriveListLayout()
    {
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStorage))]
    [NotifyPropertyChangedFor(nameof(StorageColumnWidth))]
    public partial bool IsCompact { get; set; }

    public bool ShowStorage => !IsCompact;

    public GridLength StorageColumnWidth => IsCompact ? new GridLength(0) : new GridLength(150);
}

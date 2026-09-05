using CommunityToolkit.Mvvm.ComponentModel;

namespace Helix.App.Common;

/// <summary>
/// The one answer to "is the drive card compact?", for the two grids that have to agree
/// about it: the column header on the dashboard and every row in the list.
/// </summary>
/// <remarks>
/// A singleton bound with a static <c>Source</c>, because of where it is bound from. A
/// <c>ColumnDefinition</c> is a <c>BindableObject</c> but not an <c>Element</c>: it has no
/// place in the visual tree and no parent, so a <c>RelativeSource AncestorType</c> binding
/// on it has nothing to walk up and fails inside the item template — which took every row
/// with it, leaving a list that counted thirteen drives and drew none. A binding with an
/// explicit source needs neither a parent nor a binding context, only something to
/// subscribe to, and that works on any bindable object.
///
/// The page drives it from the card's measured width; <c>HomeViewModel.IsCompact</c>
/// mirrors it for the things on the page that bind through the viewmodel.
/// </remarks>
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

    /// <summary>Whether the storage-usage column is shown.</summary>
    public bool ShowStorage => !IsCompact;

    /// <summary>
    /// The storage-usage column's width — 0 when compact, so the drive's name gets the
    /// room. Bound identically by the header grid and every row grid.
    /// </summary>
    public GridLength StorageColumnWidth => IsCompact ? new GridLength(0) : new GridLength(150);
}

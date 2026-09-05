namespace Helix.App.Models;

/// <summary>
/// The column the drive list is ordered by.
/// </summary>
/// <remarks>
/// The list has always drawn a header row and never let anyone click it; the only ordering
/// on offer was ascending or descending by name, chosen in a modal, with no way to say by
/// what. These are the three columns worth ordering by.
///
/// Storage usage is not among them on purpose: what the column holds is a sentence built
/// for reading ("1.2 TB free of 43.2 TB"), and sorting a set of drives by the text of that
/// would put 9 GB after 40 TB. Ordering by real free space needs the figure behind it,
/// which the row does not carry.
/// </remarks>
internal enum DriveSortField
{
    Letter,
    Name,

    /// <summary>Connected first or last, then by letter so the order is stable.</summary>
    Status
}

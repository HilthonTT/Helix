namespace Helix.App.Behaviors;

/// <summary>
/// What a row does when a key is pressed on it. Implemented by the item template and
/// handed to <see cref="RowFocus"/>, so the behavior knows how to reach the row without
/// a static event whose subscription would have to be undone every time a recycled row
/// is handed a different drive.
/// </summary>
internal interface IRowKeys
{
    /// <summary>Returns true when the key was acted on and should go no further.</summary>
    bool OnRowKey(RowKey key);
}

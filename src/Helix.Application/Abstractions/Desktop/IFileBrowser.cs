namespace Helix.Application.Abstractions.Desktop;

/// <summary>
/// Shows a folder to the user in whatever this platform browses files with.
/// </summary>
/// <remarks>
/// Not one of the per-OS seams: the shell verb that opens a directory is the same call on
/// both heads, and only the path differs — which is <see cref="Connector.INasConnector"/>'s
/// business, since it is the thing that decided where the mount went.
/// </remarks>
public interface IFileBrowser
{
    /// <summary>
    /// Opens <paramref name="path"/> in the file manager. Fails rather than throwing when
    /// the folder is gone — a share can be unmounted between the row being drawn and the
    /// user clicking it.
    /// </summary>
    Result Open(string path);
}

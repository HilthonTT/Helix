namespace Helix.Application.Abstractions.Desktop;

public interface IFolderPicker
{
    Task<FolderPick> PickAsync(CancellationToken cancellationToken = default);
}

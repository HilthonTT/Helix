using Helix.Application.Abstractions.Desktop;

namespace Helix.App.Services;

internal sealed class FolderPickerService : IFolderPicker
{
    public async Task<FolderPick> PickAsync(CancellationToken cancellationToken = default)
    {
        CommunityToolkit.Maui.Storage.FolderPickerResult result =
            await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync(cancellationToken);

        return result.IsSuccessful ? FolderPick.At(result.Folder?.Path) : FolderPick.Cancelled;
    }
}

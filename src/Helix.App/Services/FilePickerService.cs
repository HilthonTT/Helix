namespace Helix.App.Services;

internal sealed class FilePickerService : Helix.Application.Abstractions.Desktop.IFilePicker
{
    public async Task<string?> PickAsync(string extension, CancellationToken cancellationToken = default)
    {
        var fileTypes = new FilePickerFileType(
            new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.iOS, new[] { extension } },
                { DevicePlatform.Android, new[] { extension } },
                { DevicePlatform.WinUI, new[] { extension } },
                { DevicePlatform.Tizen, new[] { extension } },
                { DevicePlatform.macOS, new[] { extension } },
            });

        var options = new PickOptions
        {
            FileTypes = fileTypes,
        };

        FileResult? file = await FilePicker.Default.PickAsync(options);

        return file?.FullPath;
    }
}

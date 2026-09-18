namespace Helix.Application.Abstractions.Desktop;

public interface IFilePicker
{
    Task<string?> PickAsync(string extension, CancellationToken cancellationToken = default);
}

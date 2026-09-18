namespace Helix.Application.Abstractions.Desktop;

public sealed record FolderPick(bool IsSuccessful, string? Path)
{
    public static readonly FolderPick Cancelled = new(false, null);

    public static FolderPick At(string? path) => new(true, path);
}

namespace Helix.Application.Abstractions.Desktop;

public sealed record TrayMenuItem(string Id, string Text, bool IsEnabled = true)
{
    public static TrayMenuItem Separator { get; } = new(string.Empty, string.Empty);

    public IReadOnlyList<TrayMenuItem> Children { get; init; } = [];

    public bool IsSeparator => string.IsNullOrEmpty(Id);

    public bool IsSubmenu => Children.Count > 0;
}

public interface ITrayIcon
{
    bool IsSupported { get; }

    event EventHandler? Activated;

    event EventHandler<string>? MenuItemSelected;

    bool Show(string tooltip);

    void SetMenu(IReadOnlyList<TrayMenuItem> items);

    void Notify(string title, string message);

    void Hide();
}

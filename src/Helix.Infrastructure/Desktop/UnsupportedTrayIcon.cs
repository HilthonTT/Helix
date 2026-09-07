using Helix.Application.Abstractions.Desktop;

namespace Helix.Infrastructure.Desktop;

internal sealed class UnsupportedTrayIcon : ITrayIcon
{
    public bool IsSupported => false;

    public event EventHandler? Activated
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? MenuItemSelected
    {
        add { }
        remove { }
    }

    public bool Show(string tooltip) => false;

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
    }

    public void Notify(string title, string message)
    {
    }

    public void Hide()
    {
    }
}

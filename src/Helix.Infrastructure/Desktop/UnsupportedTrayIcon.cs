using Helix.Application.Abstractions.Desktop;

namespace Helix.Infrastructure.Desktop;

/// <summary>
/// The tray icon on a head that has no tray to put one in. Accepts every call and does
/// nothing.
/// </summary>
/// <remarks>
/// macOS keeps status items in the menu bar through <c>NSStatusItem</c>, which is AppKit
/// and therefore out of reach of a Mac Catalyst process without shipping a separate
/// plugin bundle. Rather than leave <see cref="ITrayIcon"/> unbound and have the
/// composition root throw at sign-in, the Catalyst head binds this and reports
/// <see cref="IsSupported"/> as false — callers check that and skip the feature, so
/// nothing half-working appears in the UI.
/// </remarks>
internal sealed class UnsupportedTrayIcon : ITrayIcon
{
    public bool IsSupported => false;

    // Never raised. Declared to satisfy the interface; the compiler warning about an
    // event that is never used is suppressed by the explicit add/remove accessors.
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

    public void Show(string tooltip)
    {
    }

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

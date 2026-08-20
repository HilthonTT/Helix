namespace Helix.Application.Abstractions.Desktop;

/// <summary>One entry in the tray icon's context menu.</summary>
/// <param name="Id">
/// Echoed back through <see cref="ITrayIcon.MenuItemSelected"/> when the entry is
/// clicked. The tray implementation never interprets it, which is what keeps the
/// platform code free of any knowledge about drives.
/// </param>
public sealed record TrayMenuItem(string Id, string Text, bool IsEnabled = true)
{
    /// <summary>A dividing line. Carries no id, so it can never be selected.</summary>
    public static TrayMenuItem Separator { get; } = new(string.Empty, string.Empty);

    public bool IsSeparator => string.IsNullOrEmpty(Id);
}

/// <summary>
/// A status icon in the system tray (Windows) or its equivalent, with a context menu
/// and the ability to raise a passive notification.
/// </summary>
/// <remarks>
/// Helix spends most of its life doing nothing visible — watching shares and putting
/// them back when they drop — so this is where it reports what it did, and the only
/// place the user can act on a drive without bringing the window back.
///
/// The interface is deliberately dumb: it renders a list of labels and reports which
/// one was clicked. Deciding what the menu should say, and what a click means, belongs
/// to the presentation layer, which is the only place that can reach a use case.
///
/// Implementations must tolerate being called from any thread, and raise their events
/// on an unspecified one — subscribers that touch bound properties marshal themselves.
/// </remarks>
public interface ITrayIcon
{
    /// <summary>
    /// False where the platform has no tray to sit in. Callers should skip the whole
    /// feature rather than showing controls that will do nothing.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Raised when the icon itself is clicked — "bring the window back".</summary>
    event EventHandler? Activated;

    /// <summary>Raised with the <see cref="TrayMenuItem.Id"/> of the entry clicked.</summary>
    event EventHandler<string>? MenuItemSelected;

    /// <summary>Shows the icon, or updates its tooltip if it is already showing.</summary>
    void Show(string tooltip);

    /// <summary>Replaces the context menu. Safe to call as often as the state changes.</summary>
    void SetMenu(IReadOnlyList<TrayMenuItem> items);

    /// <summary>Raises a passive notification from the icon.</summary>
    void Notify(string title, string message);

    /// <summary>Removes the icon. Called on sign-out, and at shutdown.</summary>
    void Hide();
}

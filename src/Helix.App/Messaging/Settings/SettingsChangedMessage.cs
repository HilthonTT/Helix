namespace Helix.App.Messaging.Settings;

/// <summary>
/// A setting was written and accepted by the store.
/// </summary>
/// <remarks>
/// Carries nothing, like the drive messages beside it: a listener re-reads the settings
/// it cares about rather than being handed a value it would then have to keep in step
/// with the rest.
///
/// It exists for the settings that have to be answered synchronously — whether the close
/// button hides to the tray, and whether the tray says so — which cannot wait on a query
/// at the moment they are needed and so are cached in <c>TrayIconService</c>. A cache
/// that only refilled at sign-in would leave a switch on the page next door with no
/// effect until the next one.
/// </remarks>
internal sealed record SettingsChangedMessage;

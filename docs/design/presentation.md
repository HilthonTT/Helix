# Presentation layer

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Closing the window

`Settings.CloseToTray` decides what the title bar's close button means: hide to the tray,
or quit. On, because everything Helix does unattended — reconnecting a dropped share,
warning about a full pool, locking itself — stops the moment the process does, and a NAS
tool that only works while its window is open is a NAS tool that does not work. Off is
for the user who wants the button to mean what it says.

`Settings.NotifyOnMinimizeToTray` is whether the tray says so when the window is put away
there, once per session, and covers the auto-minimize countdown as well as the close
button — both go through `TrayIconService.NotifyHiddenToTray`. On, because a window that
vanishes from the taskbar unexplained reads as a crash.

Both are cached in `TrayIconService` and read from there synchronously, because the
window's `Closing` handler is a WinUI event that has to decide before it returns and
cannot await a query. The cache is filled per sign-in and refreshed on
`SettingsChangedMessage`, which `SettingsDisplay` sends after any accepted write — a
cache that only refilled at sign-in would be a switch that did nothing until the next
one. A failed read leaves the last known values rather than reverting to the defaults.

Neither switch is offered on macOS, although the menu bar icon is: `SettingsViewModel
.SupportsTray` is `ITrayIcon.IsSupported` **and** Windows, because both switches are about
hiding the window, and Catalyst has no public way to hide one — the rows would be shown
and ignored. `BaseViewModel.MinimizeApp` carries the same guard, or the countdown would
announce "still running in the tray" over a window that never moved.

The menu also rebuilds on `CheckDrivesStatusMessage`. A connect from the window holds a
suppression, so the monitor never reports it, and the tray kept offering "Connect Z:" for a
drive the row had just mounted — and `ToggleDriveAsync`, which reads the live state, then
*disconnected* it under that label.

Every tray-menu action reports its failure through the balloon (`TrayIconService.Report`)
and a warning in the log; the tray is used while the window is away, so there is no banner
to land on, and a "connect all" with a wrong password used to produce nothing at all. The
dashboard's "disconnect all" reads its result too — connect-all always did. At logon Helix
can be up before the taskbar is, and the shell refuses the icon; `WindowsTrayIcon.Show`
now adds the icon whenever it is missing and the service retries `Show` every ten seconds
for three minutes, because the earlier "no tray" verdict left an icon with an empty menu
and a close button that quit.


## Helix.App (presentation)

Standard MAUI layout, feature-foldered inside `Views/` and `ViewModels/`:

```
App.xaml, AppShell.xaml, MauiProgram.cs, GlobalUsings.cs
Behaviors/     attached behaviors used from XAML
Common/        ScopedHandler, PageNames, PresentationAssembly, StorageUsageHelper, WindowSizing,
               MainWindow, DrivePlatform, AppLog
Controls/      custom controls and layouts (NavItem, ChartView, HorizontalWrapLayout,
               NotificationHost, NumberField)
Converters/    IValueConverter implementations
Extensions/    DependencyInjection (AddPresensation)
Icons/         IconFont glyph constants
Localization/  LocalizationResourceManager, TranslateExtension, CultureSwitcher
Messaging/     CommunityToolkit.Mvvm messages, by feature
               Auditlogs/, DriveGroups/, Drives/, Navigation/, Notifications/,
               Schedules/, Settings/, Storage/, Updates/, Users/
Models/        observable display models bound by the views
Platforms/     MAUI platform heads
Resources/     AppIcon, Fonts, Images, Languages, Splash, Styles
Services/      DriveWatchdog, TrayIconService, StorageAlertService, ScheduleService,
               IdleLockService, HotkeyService, CommandListener, EstateStatus, ModalHost,
               PassphrasePromptService, Notifier
Theming/       ThemeChoice, ThemePalette, ThemePalettes, ThemeSwitcher
ViewModels/    BaseViewModel + Auditlogs/, Drives/, Insights/, Settings/, Users/
Views/         pages, modals and item templates: Auditlogs/, Drives/, Insights/, Settings/, Users/
```

A view and its viewmodel sit in the same feature folder under their respective roots — `Views/Drives/HomePage.xaml` pairs with `ViewModels/Drives/HomeViewModel.cs`.

`GlobalUsings.cs` imports `Helix.App.Common` and `Helix.App.Localization` alongside the SharedKernel namespaces, so `ScopedHandler`, `PageNames` and `LocalizationResourceManager` need no per-file using.

## What the sidebar carries

The flyout's flexible row was empty, and four things live in it now, each because the app
already knew something it had no way to say from anywhere but the dashboard.

**The groups** are listed and connectable from the sidebar. `TrayIconService` already
renders the same set as a menu, so this is that list with the window up — the dashboard
strip is where a group is *managed*, the sidebar is where one is *used* from whatever page
the user happens to be on. Both rows go through `Common/DriveGroupActions`, which is the
whole of what surrounds `ConnectDriveGroup` — the busy flag, the connectivity refresh, the
per-drive notification, the failure banner — so the strip and the sidebar cannot drift.

**The overview** — connected count, storage total, a low-space warning — is held by
`Services/EstateStatus`, a singleton, for the length of the session. The dashboard's own
tiles compute the same numbers off `HomeViewModel`'s master list, and those die with the
page: navigating to the settings or the audit log left the user with no sight of the NAS at
all. Refreshes are driven by the messages that mean the estate changed
(`CheckDrivesStatusMessage`, `DriveCreatedMessage`, `DriveDeletedMessage`) rather than by a
timer, they are serialized, and one arriving mid-probe is *remembered rather than dropped* —
a "connect all" raises one per drive and the reading taken before the last of them mounted
would otherwise be what stayed on screen. The low-space light is not a probe of its own: it
is `StorageAlertService`'s own 15-minute check, published as `StorageAlertsChangedMessage`,
because a second sweep of the shares for the same answer is exactly what `GetStorageAlerts`
exists to avoid.

**The update badge** is the result of one check per signed-in session, run from
`AppShell.BeginSession`. It is only ever a badge: tapping it navigates to the settings page
and sends `ShowUpdatesMessage`, which runs the check-download-install flow that page already
owns, rather than carrying a second copy of it. `SettingsViewModel` sends
`UpdateCheckedMessage` back after any check it runs, so a badge cannot outlive a check the
user ran themselves. While an install is downloading, the badge shows the same status line the settings page
does, with a progress bar under it: `SettingsViewModel` sends `UpdateProgressMessage` whenever
`UpdateStatus` changes, so the progress stays in view after the user navigates away from the
page that started it. An empty status puts the badge back to "Update available". A check that cannot reach GitHub is logged and nothing is shown — the
user did not ask for it.

**Lock** sits beside Logout. `IdleLockService.LockNowAsync` is deliberately independent of
the idle poller: the lock is offered whether or not the platform can report idle time, and
whether or not `IdleLockMinutes` is set to never lock on its own. Before it, the only way to
lock was to wait out the timer, and the alternative — signing out — stops the watchdog, the
tray and the storage alerts, which is precisely what locking does not.

`AppShell.BeginSession` and `EndSession` bracket all four, driven from `OnNavigated`. The
sign-in pages end the session; **`LockPage` does not**, for the same reason the watchdog
keeps running behind it — the session is live, the drives are mounted, and only the screen
is covered.

Everything the flyout binds must be **public**, including `EstateStatus` and
`DriveGroupDisplay`. XamlC will not resolve an internal member for a compiled binding on a
public host type; it degrades to `XC0045` and a binding that silently does nothing.

## The auto-minimize countdown

`ICountdownService` is paused by `HomePage.OnDisappearing` and picked back up by
`InitializeCountdownAsync` in `OnAppearing`. The dashboard is the only page that shows the
countdown and the only one that can call it off, so a countdown left running while the user
reads the settings page minimizes the window out from under them.

Settings are re-read on every resume rather than on first start, so auto-minimize switched
off — or its timer changed — while the user was away is honoured the moment they come back:
off clears the countdown outright, a changed `TimerCount` restarts rather than resuming a
stale count. A countdown the user **cancelled**, or one that already fired and minimized,
is not auto-resumed; it comes back showing its "Continue" affordance, which is what
cancelling meant. A return to the dashboard while the first `OnAppearing` is still starting
the session's services is remembered and the countdown resumed once that finishes, since the
re-entrant call returns early and the pause from leaving would otherwise stand.

`System.Timers.Timer` can raise an `Elapsed` that was already queued when `Stop` was called.
`CountdownService` ignores a tick that arrives while the timer is off: without that, leaving
the dashboard on the last second paused the countdown and then let it finish anyway, and the
window minimized from under the settings page — the thing the pause exists to prevent.

## What the dashboard gives room to

The stat row is **two cards, not three**. The auto-minimize countdown used to hold the
third, level with how much storage the NAS has and how many drives are up — app chrome
given the same weight as the two facts the page exists to report, and shown even to the
users who have auto-minimize switched off and will never see it move. It is a chip in the
page header now, present only while something is counting (`BaseViewModel.ShowCountdown`),
carrying the same cancel and resume affordances the card had.

`CountdownDisplay` renders it as `m:ss`. It was `$"{SecondsRemaining} seconds"` — English
in an app translated into five languages, and "1 seconds" on the way past. Digits and a
colon are the same in every locale, and the chip's icon and tooltip carry the meaning.

The drive row's second line is the **host and** the last-connected stamp. A name is
whatever the user called it, so two drives on two different NASes were told apart only by
that; the address is the thing they actually differ by.

## Numbers in the settings page

The four numeric preferences bind to `Controls/NumberField` rather than a bare `Entry`,
and it is the answer to three separate problems with what was there.

The **unit** lived in the row's title — "Timer count in seconds" — so the box was a number
with no dimension, and the retention and idle-lock rows never said what they counted at
all. **Zero means something** in three of the four — keep every audit log, never warn about
space, never lock — and nothing on screen said so; `OffText` replaces the unit with that
meaning the moment the value reaches zero. And the **bounds were only enforced after the
write**: the handler rejected the value and the row rolled back, which is a failure banner
for something the control could simply not have allowed. The low-space ceiling is bound
from `Settings.MaximumStorageAlertThresholdPercent` rather than written into the page, so
the two cannot drift. `UpdateSettings` still validates — this is a keyboard, not a trust
boundary.
`UpdateSettings` touches the startup and desktop shortcuts **only when their switch
moved**: rewriting both on every save meant a Startup folder that had become unwritable
failed the timer, the retention and the language with an error about a shortcut nobody
had touched. Language has a rollback like every other setting, switching the culture
back, since the page had already switched it before the write.

The typed value is committed on Enter or on leaving the field, never per keystroke.
The `−`/`+` step is per field: the countdown moves by one second, because a user tuning it
wants 12 rather than 10 or 15; days, percent and minutes move by a stride.
`SettingsDisplay` debounces these by 500ms precisely because "9" on the way to "90" used to
be saved and acted on; committing whole values means there is no such intermediate, and the
timer stays as a backstop rather than as the thing standing between the user and a wrong
setting.

Every write sends the whole settings row, and `SettingsDisplay` fills the four numbers and the
language from what was **last persisted**, not from what the fields currently show. A number
still inside its debounce used to ride along on whichever switch was flipped next: it was saved
without its own confirmation or its `_persisted` baseline moving, and if it was the one the
handler refused, the switch rolled back with an error about a field the user had not finished
typing in.

The settings page builds a fresh `SettingsDisplay` every time it appears, so
`LoadSettingsAsync` first **flushes** the old one (`FlushAsync`): a debounce still running
is saved at once and any save already queued is awaited. Without it a value committed just
before leaving the page was read back stale, saved a moment later by the discarded
instance, and then overwritten by the next switch the user flipped.

Only these four confirm themselves, through `Notifier`. A switch is its own confirmation —
it is sitting there in its new position — but a number typed into a box looks identical
whether it was stored or thrown away, and the write lands well after the keystroke that
caused it.

## Global shortcuts

`Settings.GlobalHotkeys` turns on **Ctrl+Alt+Shift+C** (connect every drive) and
**Ctrl+Alt+Shift+D** (disconnect every drive) from any application, through the SharpHook
hook `MauiProgram` already runs. Off by default: a system-wide chord is something a user
should ask for, not discover. Three modifiers because Ctrl+Alt is AltGr on the European
layouts Helix is translated for, and AltGr+C and AltGr+D type characters there. The hook
does not swallow keys, so another application bound to the same chord still gets it.

`HotkeyService` answers only while a session is live and **not while the idle lock is up** —
the lock exists to stop someone at an unattended keyboard, and "disconnect every drive" is
exactly what it should stop. Like the tray's own "disconnect all" it does not confirm: the
point is that the window is not in front. The outcome goes to the tray balloon when the icon
is up and to the banner otherwise, and a repeat while one is running is ignored rather than
queued, since a held key auto-repeats.

## The command line

`Helix.App.exe --connect-all`, `--disconnect-all`, `--connect <drive>`, `--disconnect <drive>`,
`--connect-group <name>`, `--disconnect-group <name>`, `--wake <drive>`, `--status`, `--show`,
`--quit`, `--help`. A drive is named by letter (`Z`, `Z:`) or by name; a name two drives share
is refused with their letters rather than guessed at. Task Scheduler, a backup job's pre-script
and a Stream Deck button are what this is for.

It drives the **running** instance, and only that. The second process that `MauiProgram`'s
single-instance mutex already turned away now carries its arguments to the first over a named
pipe (`CommandListener`, `Helix.App.Command.<session id>`, `PipeOptions.CurrentUserOnly` on
both ends) and prints the one-line answer. There is no cold path: a command given with no Helix
running says so and exits 1, because every drive's credentials are behind the account password
and nothing without a signed-in session can mount anything. For the same reason a command is
refused while signed out and while the **idle lock** is up — the lock stops an unattended
keyboard, and a script is one. `--help` and a mistyped flag are answered by the client itself,
so a typo is not reported as "Helix is not running".

The pipe **replaced** the `EventWaitHandle` a second launch used to set. An event carries no
payload, and two mechanisms for "a second copy started" is one too many; a bare launch is now
`--show` over the same pipe, which restores a window hidden to the tray the way the event did.
Each connection is answered on its own task. The listener used to take one at a time, so a
`--connect-all` waiting out a mount timeout left the next client — a second launch's `--show`
included — to time out and report "did not answer". A request that throws is still answered,
with a failure, and `--quit` writes its reply before it exits rather than racing it.
The client still calls `ShowWindow`/`SetForegroundWindow` on the other's visible window itself,
and grants it `AllowSetForegroundWindow` first — the freshly launched process is the one
Windows lets take the foreground, not the one sitting in the tray.

Every command is also a **`helix://` link** (`helix://connect/Z`, `helix://connect-group/Office`,
`helix://disconnect-all`), parsed by `CommandRequest.FromUri` into the same request with
`FromLink` set, and that flag rides the pipe as a third field. On Windows `Common/UrlScheme`
writes `HKCU\Software\Classes\helix` at every start, so the scheme follows the install across
an update's folder swap; a link then launches a second process like any other command and is
forwarded. On macOS the scheme is in `Info.plist` and the link arrives in the running process
through the `OpenUrl` lifecycle event, straight to `CommandListener.RunLinkAsync` — which is how
links give the Mac the command line it otherwise cannot have.

A link is not a command typed at a console, and three things follow. It is answered through the
**tray balloon or the banner**, in the user's language, since there is nobody reading stdout. A
link opened with Helix closed **starts Helix** and drops the request, rather than failing at a
user who just clicked something. And because any web page can ask a browser to open one, a link
that **disconnects or quits** brings the window forward and asks first (`NeedsConfirmation`);
the flags never ask, since a local script already runs as the user. Connecting, waking and the
status are harmless enough to run unasked. Control characters are stripped from a link's target
so a crafted one cannot forge the pipe's field separators.

The answer goes to the parent console through `AttachConsole`, success on stdout and failure
on stderr, and the exit code is 0 or 1. Helix is a GUI executable, so **PowerShell and cmd do
not wait for it** unless asked: `Start-Process -Wait -PassThru`, `start /wait`, or piping the
output. The replies are English, like every other CLI on the machine, while the Wake NAS banner
and everything else a window shows stay in `AppResources`. Windows only: Mac Catalyst has no
single-instance mutex, and a second `open` of a running `.app` does not forward its arguments.

## The tray's folder menu

The tray menu carries **Open folder**, a submenu of the drives that are mounted from their
share. `TrayMenuItem.Children` is what makes a submenu; `WindowsTrayIcon` numbers every leaf
of the tree in one sequence as it builds the popup, so a selection still maps back to one
item id. Only mounted drives are listed — a folder that is not there is not a menu item.

## Platform-specific presentation code

The XAML, viewmodels, converters and behaviours are shared verbatim; only these carry an
`#if`, and each has a working macOS path or a deliberate no-op:

- `MauiProgram` — the WinUI lifecycle hook, the `EntryHandler` chrome tweak and the link
  scheme registration are `#if WINDOWS`; the `OpenUrl` hook that delivers a link is
  `#if MACCATALYST`. The SharpHook startup is not: as of SharpHook 8 the package ships a Mac
  Catalyst assembly and the libuiohook natives for it, so the global hook runs on both
  heads.
- `App.CreateWindow` — Catalyst sizes its window here from `Common/WindowSizing`, the
  same rule the Windows lifecycle event applies through `AppWindow`. Change the rule in
  one place and both heads follow.
- `Common/MainWindow` — hides, minimizes and restores the app window; Windows only,
  except `Restore`, which on macOS activates the app (`NSApplication
  activateIgnoringOtherApps:`) so the menu bar's Open item brings Helix forward. Mac
  Catalyst exposes no public API for hiding a window scene, so `BaseViewModel.MinimizeApp`
  runs its countdown and then does nothing on macOS rather than reaching for a private
  selector. On Windows it hides to the tray while `TrayIconService` is running, because
  the icon is then the way back, and falls back to a plain minimize when it is not. The
  title bar close button follows the same rule: `MauiProgram.OnWindowClosing` cancels the
  close and hides to the tray while the icon is up, and lets the window close when it is
  not, so the app is never hidden with no way back. The tray Exit item is the only real
  quit, and it goes through `MainWindow.Exit` — the `IsExiting` flag is what stops that
  shutdown being turned back into a hide. `Settings.CloseToTray` turned off is the other
  way out: the close still cancels, but it then goes through `MainWindow.Exit` too, so
  the icon comes down with the process rather than sitting in the tray until somebody
  mouses over it.
- `Common/DrivePlatform` — the one flag the shared drive modals bind to, so the
  "reconnect at sign-in" switch is hidden rather than shown-and-ignored on macOS.
- `Behaviors/Hover` (hand cursor) and `Services/ModalHost` (Escape-to-dismiss) — Windows
  only, no-ops elsewhere.
- `AppShell` — on Windows it clears the visual-state transitions of the `SplitView` inside
  Shell's `NavigationView` once the platform view loads. The sign-in pages run with the
  flyout `Disabled` and the session switches it to `Locked`, which takes the pane from
  closed to open-inline. WinUI animates that, so the first page after signing in slid the
  sidebar in and reflowed the page under it. Without the transitions the pane snaps into
  place.

The `LoginPage`/`RegisterPage` Ctrl+Enter shortcut used to be in that list and no longer
is. The hook is started once in `MauiProgram` and both pages subscribe to the singleton,
because libuiohook allows one running hook per process. It is asked for as
`GlobalHookType.Keyboard`: Ctrl+Enter is all it is for, and the mouse half would put every
pointer move across the native boundary for nothing. macOS refuses a global hook until the
app is granted Accessibility access, so on a Mac where it has not been, `RunAsync` faults
at startup, that is logged with what to do about it, and everything else carries on —
subscribing to a hook that never started simply never raises. Linux is not a question
Helix can answer: libuiohook supports it, MAUI has no Linux head.

The Catalyst head ships with the **App Sandbox disabled** (`Platforms/MacCatalyst/Entitlements.plist`).
A sandboxed process cannot mount a network filesystem, write a LaunchAgent or touch the
real Desktop, so sandboxing it would break every platform service at once. That makes the
macOS build Developer ID / direct distribution, not Mac App Store.

## A `Picker` bound to a value needs its items first

`UpdateDriveViewModel` loads `AvailableLetters` **before** it assigns `Drive`. A `Picker`
whose `SelectedItem` arrives while its `ItemsSource` is empty, or still holds the previous
drive's letters, selects nothing, and replacing the list afterwards does not select it
either — so the sheet opened with no letter chosen and every edit had to start by choosing
the same letter again.

## XAML conventions

XML namespace prefixes are consistent across every page and modal:

- `vm:` — the file's viewmodel namespace (`x:DataType`)
- `views:` — sibling views in the same feature
- `l10n:` — `Helix.App.Localization`, for `{l10n:Translate Key}` and `LocalizationResourceManager`
- `controls:`, `behaviors:`, `converters:`, `icons:`, `models:` — the matching folders
- `local:` — the root `Helix.App` namespace (only `AppShell.xaml` needs it)


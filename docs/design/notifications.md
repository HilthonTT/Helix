# Logging and telling the user

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Logging

Use `ILogger<T>` — **never `Debug.WriteLine`**, which no released build writes anywhere and
which left every unattended reconnect failure unreportable. A `FileLoggerProvider` in
`Infrastructure/Diagnostics` writes a dated file under `%LOCALAPPDATA%/.../logs`, kept for
14 days, and the settings page exports them as a zip through `IDiagnosticsLog`.

`LogFileWriter` rolls to `helix-<date>-<n>.log` at 2 MB, and the sequence lives in memory. At
startup it steps past files that are already full before opening one; it used to reopen
`helix-<date>.log` regardless and push one line into each full file on its way back to the
live one.

Anything the container constructs takes `ILogger<T>` as a constructor dependency. Pages,
viewmodels and static helpers are built by MAUI rather than DI and use `AppLog.For<T>()`
instead — that is the only reason it exists, so do not reach for it from a type that could
have taken the dependency properly.

Release builds log Information and above; Debug builds also log Debug. Keep credentials,
hosts and share names out of log messages beyond what a drive letter already reveals — the
user is expected to send these files to a stranger.

## Telling the user what happened

Outcomes go through `Notifier`, which puts a dismissible banner on the page — not a
`DisplayAlert`. `BaseViewModel.DisplayErrorAsync` and `DisplaySuccessAsync` still exist
and still return a `Task`, because every call site already awaits them; they route to the
banner now. **Do not reintroduce an alert for a result.**

A dialog for a success is a keystroke the user owes the app for work they asked for and
watched happen. Worse, WinUI throws when a second alert is raised while one is showing,
which "connect all" over thirteen shares walks straight into — `DriveTemplate` carried a
try/catch for exactly that, and it is gone. Modal dialogs are kept for the cases that are
genuinely a **question**: deleting a group, installing an update, the export passphrase.

`Notifier` marshals to the UI thread itself, because most of its callers are background
work — the watchdog, the storage sweep, an `async void` handler — and none of them should
have to know that. A message nobody displayed is held for 30 seconds rather than dropped:
a failure raised mid-navigation, or before the first page is up, then lands on the page
that arrives. That is what makes the WinUI last-resort handler in `App.xaml.cs` reportable
at all — it previously had no window to raise an alert on.

`NotificationHost` is the banner stack, one per page in the **last child of the root
grid**, so a banner sits above the modal layer: a failure raised from inside a sheet has
to be readable without closing it. Only the host whose page is `Shell.Current.CurrentPage`
accepts a message — Shell keeps visited pages alive, so a plain broadcast would leave a
banner waiting on three pages the user is not on. `LockPage` deliberately has none; it
renders its own error inline.

Banners dismiss themselves, a failure getting noticeably longer than a success, and the
stack is capped so a sweep failing over thirteen shares cannot bury the page it is
reporting on. Nothing is lost when one goes: the audit log and the diagnostics file are
what the record is for.

Every string either side of this is in `AppResources`. The app is translated into five
languages and was still saying "Something went wrong!" in English at eleven call sites.

A banner can also be **taken back**. `Notifier.Retract` names one line of banner text and
`NotificationHost` removes it: a banner that was only that line is dismissed, and one
carrying several — the aggregated failures of a "connect all" — is rewritten without it,
since the other twelve drives' failures are still true. It works on the held queue as well,
so a message retracted before any page displayed it is never shown. The line is the unit
because that is what `DriveMountBatch` composes (`"{Letter}: {description}"`), and
`DriveTemplate` now prefixes a single row's failure the same way — a bare "Connection timed
out." named no drive and could not be told apart from another drive's.


# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Helix is a .NET MAUI desktop app for managing connections to NAS drives. It ships two heads:

- **Windows** — `net10.0-windows10.0.19041.0`. Open `Helix.slnx` in Visual Studio 2022 to build/run.
- **macOS** — `net10.0-maccatalyst`. Build on a Mac with Xcode installed; Catalyst cannot be compiled from Windows.

`Helix.App` and `Helix.Infrastructure` each gate their target frameworks on the host OS, so `dotnet build` produces the right head on either machine without a `-f` switch. Adding a target framework to one of those projects means adding it to both.

## Commands

Build and test from the repo root:

```bash
dotnet build Helix.slnx
dotnet test Helix.slnx
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj
dotnet test tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
dotnet test tests/ArchitectureTests/ArchitectureTests.csproj
```

Run a single test:

```bash
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~CreateDriveTests"
```

EF Core migrations target `Helix.Infrastructure` but `Microsoft.EntityFrameworkCore.Tools` is referenced from `Helix.App`, so migrations are run with the App as the startup project:

```bash
dotnet ef migrations add <Name> --project src/Helix.Infrastructure --startup-project src/Helix.App
```

Running the MAUI app itself is normally done via Visual Studio 2022 (`Helix.App` startup project), not `dotnet run`, because of the MAUI/Windows packaging configuration.

The test projects target `net10.0-windows10.0.19041.0`, so the suite only runs on Windows. On a Mac, build the app project on its own — the solution also contains those Windows-only projects:

```bash
dotnet build src/Helix.App/Helix.App.csproj
```

CI covers both: a Windows job builds and tests, a macOS job compile-verifies the Catalyst head.

## Build configuration

Two repo-root files own the shared MSBuild configuration; keep project files free of anything they already cover:

- `Directory.Build.props` — properties every project inherits (`ImplicitUsings`, `Nullable`).
- `Directory.Packages.props` — Central Package Management. Every NuGet version lives here and `.csproj` files reference packages *without* a `Version` attribute.

`Helix.App.csproj` sets `SatelliteResourceLanguages` to the cultures Helix is translated
into, and `_HelixPruneWindowsAppSdkCultures` finishes the job: the property trims the .NET
satellite assemblies, but the self-contained Windows App SDK also copies WinUI's own
`.mui` resources as one folder per culture — 86 of them, for dialogs in languages the app
cannot be switched to — and those are `None` items the SDK's targets add, so they are
removed by matching on the language in their `Link`. Together they took a published
install from 119 culture folders to 13 and 650 files to 494; file count, not size, is what
an antivirus scan, a copy and a backup pay for. **Add a culture to both places** when adding
an `AppResources.<culture>.resx`. The prune target uses `String.Contains` rather than a
regex on purpose: MSBuild's property-function parser mis-parses a `(…)` group inside a
quoted argument and the condition silently matched nothing.

## Architecture

Clean Architecture with four layers plus a SharedKernel. Dependencies only point inward, and this is enforced by `tests/ArchitectureTests/Layers/LayerTests.cs` using NetArchTest — do not break those rules.

**Namespaces mirror folders exactly, in every project.** The architecture tests guard this for Infrastructure (`tests/ArchitectureTests/Infrastructure/InfrastructureTests.cs`) and for Application handlers (`tests/ArchitectureTests/Application/ApplicationTests.cs`). When you move a file, move its namespace with it.

Each layer exposes an `XxxAssembly.cs` marker type (`DomainAssembly`, `ApplicationAssembly`, `InfrastructureAssembly`, `PresentationAssembly`) used by the architecture tests and for assembly scanning.

### SharedKernel

Framework-free primitives, in three folders/namespaces:

- `SharedKernel.Abstractions` — `IAuditable`, `IDateTimeProvider`.
- `SharedKernel.Primitives` — `Entity`, `Enumeration`, `Ensure`.
- `SharedKernel.Results` — `Result`, `Result<T>`, `Error`, `ErrorType`, `ValidationError`.

All three are imported through a `GlobalUsings.cs` in every consuming project, so individual files do not write `using SharedKernel...;`. The project deliberately has **no** MAUI or EF Core reference — do not add one.

### Helix.Domain

Entities, domain errors and repository **interfaces**, one folder per aggregate (`Auditlogs/`, `DriveGroups/`, `Drives/`, `Settings/`, `Users/`). Also framework-free: no MAUI, no EF Core, no Application/Infrastructure dependency. Error classes are plural (`DriveErrors`, `UserErrors`, `SettingsErrors`, `AuthenticationErrors`).

### Helix.Application

```
Abstractions/   Authentication, Connector, Cryptography, Data, Desktop, Diagnostics,
                Handlers, Security, Startup, Storage, Time, Updates — interfaces only
Core/           Drives, Errors, Sorting, Validation — cross-feature helpers
Features/       one folder per feature, split into Commands / Queries
                Auditlogs/{Commands,Queries}
                Diagnostics/Commands
                DriveGroups/{Commands,Queries}
                Drives/{Commands,Queries,Contracts}
                Settings/{Commands,Queries}
                Storage/{Queries,Contracts}
                Updates/{Commands,Queries}
                Users/Commands
DependencyInjection.cs
```

No dependency on Infrastructure — it consumes its own `Abstractions/` interfaces only.

#### Handler pattern (use cases)

Every use case is a `sealed class` implementing the marker interface `IHandler` (`Abstractions/Handlers/IHandler.cs`) and lives under `Features/`. The shape is consistent — match it when adding new use cases:

```csharp
public sealed class CreateDrive(IDriveRepository repo, IUnitOfWork uow, ...) : IHandler
{
    public sealed record Request(/* inputs */);

    public async Task<Result<Drive>> Handle(Request request, CancellationToken ct = default)
    {
        // 1. Validate (static Validate(request) returning Result)
        // 2. Authorize via ILoggedInUser
        // 3. Domain rule checks via repositories
        // 4. Mutate via repository
        // 5. await unitOfWork.SaveChangesAsync(ct)
        // 6. return Result/Result<T>
    }
}
```

Outcomes flow through `Result` / `Result<T>` — handlers never throw for expected failures. Errors come from static error classes (e.g. `DriveErrors`, `AuthenticationErrors`, `ValidationErrors`).

### Logging

Use `ILogger<T>` — **never `Debug.WriteLine`**, which no released build writes anywhere and
which left every unattended reconnect failure unreportable. A `FileLoggerProvider` in
`Infrastructure/Diagnostics` writes a dated file under `%LOCALAPPDATA%/.../logs`, kept for
14 days, and the settings page exports them as a zip through `IDiagnosticsLog`.

Anything the container constructs takes `ILogger<T>` as a constructor dependency. Pages,
viewmodels and static helpers are built by MAUI rather than DI and use `AppLog.For<T>()`
instead — that is the only reason it exists, so do not reach for it from a type that could
have taken the dependency properly.

Release builds log Information and above; Debug builds also log Debug. Keep credentials,
hosts and share names out of log messages beyond what a drive letter already reveals — the
user is expected to send these files to a stranger.

### Telling the user what happened

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

### The update check

`GitHubUpdateChecker` reads `/releases/latest` — unauthenticated, read-only, nothing
downloaded or installed. `/releases/latest` rather than the tag list on purpose: tags exist
for things never released, and it excludes pre-releases, which an unattended NAS tool
should not be nudging people onto.

Version comparison goes through `ReleaseVersion`, and it must. `ApplicationDisplayVersion`
is three-part (`2.2.1`) to match the release tags, Windows reports the running build padded
to four (`2.2.1.0`), and `Version` treats a missing component as **-1, not 0** — so an
unnormalized compare makes the running build "older" than the release it was built from and
announces an update to itself. Both sides are widened to four components first. A tag that
is not a version (`nightly`) is refused rather than guessed at.

Bumping `ApplicationDisplayVersion` is **not** enough on its own. What Windows reports is
stamped out of the `Package.appxmanifest` MAUI generates under `obj`, and that generated
copy is incremental on the manifest in `Platforms/Windows` — which deliberately never
carries a version, so a version bump does not invalidate it. Left alone, the app goes on
reporting the previous version in the sidebar, on the sign-in pages and to the update
check until somebody deletes `obj`; the giveaway is an `AssemblyInfo.cs` whose
`AssemblyFileVersion` is the new number while its `AppInfo.Version` metadata is the old
one. The `_HelixRefreshStampedAppxManifest` target in `Helix.App.csproj` deletes the stale
copy when its version no longer matches `ApplicationDisplayVersion.ApplicationVersion`, so
the build regenerates it — do not remove it, and expect to bump nothing but the `.csproj`.

`ApplicationDisplayVersion` is not, on Windows, where the running version comes from.
The resizetizer targets XmlPeek `Identity/@Version` out of
`Platforms/Windows/Package.appxmanifest` into
`[AssemblyMetadata("Microsoft.Maui.ApplicationModel.AppInfo.Version")]`, and that is what
`AppInfo.Current.VersionString` returns for an unpackaged build — the sidebar and the
update check both read it. MAUI only stamps `ApplicationDisplayVersion` into that
attribute when it is empty or the literal `0.0.0.0`, so **leave the manifest on the
placeholder**: a real number written there wins over the `.csproj` and the app announces
an update to itself. The stamped value is `ApplicationDisplayVersion` with
`ApplicationVersion` as a fourth component (`2.2.1.3`), which is why what is compared is
four-part and what is shown is not — `ReleaseVersion.ToDisplayString` reduces it for the
update dialog.

`Common/VersionInfo` reduces the same string back to three components for the UI, so what
is shown is exactly the tag on the releases page. It also strips a `+<commit>` suffix
first: the build emits both a file version and an informational one, and
`Version.TryParse` rejects the latter, which would otherwise put a commit hash in the UI.
Both places that display the version go through it — the sidebar footer via
`AppShell.AppVersion`, the sign-in pages via `BaseViewModel.AppVersion` — so the two can
never disagree about what is running.

### Reconnecting away from the NAS

`ReconnectDrive` asks `IHostReachability` whether the NAS answers before it tries to
mount anything, and returns `DriveErrors.HostUnreachable` when it does not. The point is
that a drive whose host is absent has not failed to connect: on the NAS's own network a
bad mount fails in milliseconds, but away from it every attempt waits out the platform's
SMB timeout and files a warning naming a share that was never going to answer.

`HostReachability` opens a TCP connection to 445, then 139, rather than pinging — ICMP is
blocked or deprioritised often enough that a ping proves less than the connection Helix is
about to make. Readings are cached for ten seconds and the in-flight task is shared, so
thirteen shares of one pool cost one handshake per sweep rather than thirteen. **Anything
that is not the host declining to answer reads as reachable**, deliberately: a false
"unreachable" would stop Helix reconnecting a drive that would have come back, which is
the one thing the watchdog exists to do.

Both connectors also probe for themselves, before the gate and the mount, so a row
connected by hand and a batch get the same amber "Unreachable" as the watchdog. That is
what lets the **mount timeout be thirty seconds** rather than five. Neither `WNetAddConnection2`
nor `NetFSMountURLSync` takes a token: a timeout only abandons the thread the mount is on,
and the mount then finishes on its own. At five seconds a NAS addressed by name — a NetBIOS
or mDNS lookup and then the session — regularly came up a moment after its row had been told
"Connection timed out", and the other twelve drives, whose gate wait was capped at the same
five seconds, burst through ungated into the credential conflicts the gate exists to
prevent. Switching the drives back to the IP address "fixed" both, which is how it was
reported. The short timeout was only ever guarding against an absent host, and the probe
answers that in two seconds, once per host.

The per-host gate is held until the **mount itself** finishes, not until the caller has
been answered: a timed-out mount is still building the server session, and releasing the
gate at the timeout let the next drive in, whose conflict handling dropped the session the
first was creating. `RunWithTimeoutAsync` hands the underlying task back through
`onStarted` for exactly that, and logs what a timed-out mount eventually did. `HostSpelling`
caches answers, including "nothing", for the life of the process, but **not a lookup that
timed out** — that switched `ConnectByHostname` off for a host until restart because DNS
was slow at logon.

The row says which of the two it is. `DriveDisplay.OfflineReason` carries
`HostUnreachable` or `Refused`, and the status pill has one for each — amber "Unreachable"
against red "Failed" — beside the plain "Disconnected" that a drive the user put down
keeps. Without it, a laptop away from its NAS showed thirteen identical red pills, which
is also what thirteen wrong passwords look like. The reason is only ever set from an
attempt that actually happened, by `DriveWatchdog` through `DriveAttemptFailedMessage` or
by the row's own connect; a drive nobody has tried stays on the plain pill rather than
being guessed at, and a drive that comes up clears it, so a new outage cannot be blamed on
the old one. The unreachable pill gets the localized sentence and the refused one the
share's own words, since the domain's error descriptions are not translated.

`DriveWatchdog` reads that error code and puts the drive back at a flat 30-second interval
without counting it as a failure, so a laptop returning to the NAS's network reconnects in
seconds rather than at whatever the exponential backoff had escalated to.
A `NotFound` from `ReconnectDrive` is terminal — the drive was deleted while queued — and
`RefreshWatchedDrivesAsync` prunes the pending queue of drives that are gone or have their
own auto-connect off, since otherwise a deleted drive was retried and logged as a warning
every five minutes for the session. Drops are handled **in parallel**; the connector's gate
serializes the mounts anyway, and one hung share used to hold every other drive's drop
record and pill behind it. `DriveAttemptFailedMessage` is applied by `HomeViewModel` to the
master list, for the same reason `DriveUpdatedMessage` is: a row scrolled out of the
recycling list or hidden by the filter has no template to hear it. It logs those at
Debug rather than Warning — on a laptop this is the ordinary state of affairs for most of
the day, and a warning per drive per sweep buries the real failures in the file the user is
asked to send on. The sweep is skipped outright when `Connectivity` reports no network.

### Diagnosing a drive that will not connect

Everything above is what Helix knows about *why* a mount failed, and until now all of it
collapsed into one pill and one banner: a drive that is off, a drive whose share was
renamed, a drive whose password is wrong and a drive whose server is held by somebody
else's session all read as "Failed". The stethoscope chip on the row runs `DiagnoseDrive`
and reports the chain step by step, so the user can answer for themselves the question
that otherwise becomes an email.

Four steps, in this order, and the order is the narrative: the server's **name**, then
**reaching** it, then the **share and credentials**, then the **drive letter**.

- **Name.** `IHostDiagnostics` asks `HostSpelling` for the host's other spelling and says
  what it found. A miss is a warning, not a failure — a home router with no PTR records
  for its leases is the normal case, and Helix mounts by what was typed regardless.
- **Reaching it.** A fresh TCP probe of 445 then 139, and it names the port that answered.
  Deliberately **not** `IHostReachability`: that one caches for ten seconds and shares its
  in-flight task so a sweep of thirteen shares costs one handshake, which is exactly wrong
  for a check the user just pressed a button to run. A failure here stops the chain and
  the share step is reported as **skipped** rather than guessed at.
- **Share and credentials.** `INasConnector.TestAsync`, whose deviceless mount never
  touches the drive's own letter. Three outcomes rather than two, and the third is the
  point: **`SessionConflict`** is now a distinct error code from the Windows connector
  rather than a sentence inside `FailedToConnect`, so "the password is wrong" and "Windows
  is signed in to this server as somebody else" stop looking identical.
- **Letter.** Always runs, even when the server never answered — a letter taken by a USB
  stick is worth knowing about while the NAS is off. It tells "already mounted from this
  share" apart from "in use by something else", which are the same letter and opposite
  problems.

The step this exists for is the **warning** on a share that tested fine.
`INasConnector.HasOtherMountsOn` answers whether another letter is already mounted from
this drive's server; when it is, Windows reuses that credential context and the stored
password is never actually checked, so the check reports success **and says so**. That is
the limit documented above — "editing a password and reconnecting that one drive will
appear to work whatever you type" — finally said out loud at the moment it is misleading
somebody. On macOS `HasOtherMountsOn` is `false` and means it: NetFS authenticates per
mount, so there is no session to inherit and nothing to warn about.

A share Helix already has mounted is reported as such and **not tested again**. Testing it
would either join its own session or conflict with it, and both answers would be about the
test rather than about the drive.

`IHostDiagnostics` is **not** a platform seam. Like `IFileBrowser`, it has one
implementation for both heads and is registered outside `AddPlatformServices()` — a TCP
connect and a DNS lookup are the same on either OS.

Nothing here composes a sentence. Each step carries a `DiagnosticStep`, a
`DiagnosticOutcome`, a `DiagnosticFinding` and at most one piece of data — a resolved name,
a port, a letter — and `DiagnosticStepDisplay` builds the sentence from `AppResources` at
display time, in the user's language, the same way `AuditlogDisplay` does. The one
exception is a rejected credential, which shows the connector's own words, because the
domain's error descriptions are not translated and paraphrasing "The network name cannot be
found" into something vaguer would lose the only part that identifies the problem.

The report is a modal sheet, which is not a contradiction of the no-alerts rule: that rule
is about announcing the *outcome of work the user asked for and watched happen*. This is a
document the user opened, reads, and closes — and it is too long to be a banner.

### Telling a deliberate disconnect from a drop

`IDriveMonitor.Suppress` hands back a handle; while it is held, the letters it names are
neither reported as changed nor used to move the baseline, and disposing it re-seeds them
from what is actually mounted. Every handler that mounts or unmounts on purpose —
`ConnectDrive`, `DisconnectDrive`, `ConnectAllDrives`, `DisconnectAllDrives`,
`ConnectDriveGroup`, and `DeleteDrive` for the persistent mapping it cancels — holds one
across the operation. **Add one to any new handler that mounts or unmounts.**

Without it the monitor cannot tell "the user pressed disconnect" from "the NAS fell over",
because both are a letter that stopped being there, and everything downstream believes the
second: the tray fires a toast per drive and, with auto-connect on, `DriveWatchdog` puts
every one of them straight back. Pressing "disconnect all" told the user seconds later
that their drives had reconnected. The same on the way up — the startup connect fired a
"reconnected" toast per drive, because the watchdog had seeded its baseline from a machine
with nothing mounted moments earlier.

The handle covers the whole operation rather than being a note filed after it: a poll
landing between the unmount and the note is the case that produces the spurious reconnect,
so there must be no window at all. Suppressions are counted, so a group going down while a
row's own disconnect is in flight cannot uncover the other's letters.

`ReconnectDrive` deliberately does **not** suppress. A drive coming back unattended is
exactly what the tray notification and the audit entry exist to report.

### The low-space warning

`Settings.StorageAlertThresholdPercent` is free space, as a percentage of a volume, below
which Helix says something; 0 turns it off. A percentage rather than a byte figure because
one install commonly watches volumes orders of magnitude apart in size.

`GetStorageAlerts` measures through the same `IStorageProbe` the dashboard uses, so it
answers in **volumes, not drives** — thirteen shares of one pool are one thing running out
of room, and warning about it thirteen times would train the user to dismiss the warning.
`VolumeUsage` carries the letters that resolved to each volume so the warning can name the
drives. Only mounted drives are measured: an unmounted one has not run out of space, it
has not been asked.

`StorageAlertService` runs it on its own 15-minute timer — free space moves over days, and
each reading is a blocking call against a share — and remembers which volumes it has
already warned about, so a full pool is reported once rather than every quarter of an hour.
A volume that recovers is forgotten, so it can warn again months later.
"Recovers" means **measured and found fine**: `StorageAlertReport` carries every volume the
check measured alongside the alerts, and only those are eligible to be forgotten. A pool
whose drives were unmounted for one check, or whose probe ran past the timeout, is absent
from the alerts without having recovered, and forgetting it re-fired the warning. It starts a minute
after the dashboard does, because the drives are still being connected at that moment.

### Closing the window

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

Neither switch is offered on macOS: `SettingsViewModel.SupportsTray` reports
`ITrayIcon.IsSupported`, and the rows are hidden rather than shown and ignored.

Every tray-menu action reports its failure through the balloon (`TrayIconService.Report`)
and a warning in the log; the tray is used while the window is away, so there is no banner
to land on, and a "connect all" with a wrong password used to produce nothing at all. The
dashboard's "disconnect all" reads its result too — connect-all always did. At logon Helix
can be up before the taskbar is, and the shell refuses the icon; `WindowsTrayIcon.Show`
now adds the icon whenever it is missing and the service retries `Show` every ten seconds
for three minutes, because the earlier "no tray" verdict left an icon with an empty menu
and a close button that quit.

### Drive groups

A `DriveGroup` is a named set of drives — "Home", "Office" — connected or disconnected in
one action from the dashboard strip, the group manager sheet or the tray menu.
`ConnectDriveGroup` does both directions, because everything around the mount is identical
either way and only the call in the middle differs. A drive's own `AutoConnect` flag does
**not** apply: that flag holds a drive back from the *unattended* passes, and pressing a
group button is as deliberate as it gets.

Membership is a list of drive ids on the group, stored as a **primitive collection** (one
JSON column) rather than a join table to `Drives`. A group does not own its drives: several
groups may name the same one, and deleting a drive must not delete the groups it was in.
Readers resolve the ids against the drives that exist, so an id left behind by a deleted
drive disappears from the group rather than becoming an error — `DeleteDrive` prunes them
anyway, to stop a long-lived install accumulating ids that name nothing.

### Acting on several drives at once

Everything used to be all-or-nothing — every drive, or one row at a time — with saved
groups as the only way to act on a subset. A group is something you set up in advance for a
set you use repeatedly; "these three, now" had no answer that did not involve creating a
group and leaving it behind. Rows carry a tick, and `ConnectDrives` takes the ids.

`ConnectDrives` and `ConnectDriveGroup` are the same work reached two ways, so everything
below the list of drives lives in `Core/Drives/DriveMountBatch`: which of the set are in the
wrong state, one suppression covering the whole batch, the mounts in parallel, the stamps
on one thread, the failures aggregated into a single error. What stays in each handler is
how the list was arrived at. The batch is a static helper rather than a handler because it
does **no authorization and no lookup** — the caller has already established that these
drives exist and belong to the signed-in user, and nothing that has not done so may reach
it.

Like a group, a selection ignores each drive's own `AutoConnect`: that flag holds a drive
back from what Helix does unattended, and ticking a row is as deliberate as it gets. Ids
that name nothing are skipped rather than failing the batch — a drive can be deleted from
another window while a selection is held — and the ids are de-duplicated, since a repeated
one would otherwise be mounted twice.

The selection bar **replaces** the all-drives chips rather than sitting beside them. Two
pairs of Connect/Disconnect on one line, one meaning "every drive" and the other "the three
you ticked", is the kind of ambiguity that gets a NAS unmounted by accident; for the same
reason the surviving chips now say "Connect all" and "Disconnect all" rather than "Connect"
and "Disconnect". The selection is deliberately **not** cleared after an action: connecting
three drives and then wanting to disconnect the same three is the common second act.

Unmounting is confirmed, in a dialog — the one thing on the page that is a question rather
than a result. Deleting a single drive was confirmed and unmounting thirteen at a stroke
was not, which had the risk backwards: an unmount pulls the filesystem out from under
whatever has a file open on it. The confirmation is skipped when nothing would actually
come down, because a confirmation for a no-op teaches the user to dismiss them unread, and
it has a singular and a plural form rather than one string with a number in it. The tray's
own "disconnect all" is not confirmed and should not be: there is no window to put a dialog
on when it is used.

### Importing over live mappings

`ImportDrives` and `CreateDrive` refuse a letter the connector reports as mounted — a USB
stick, an optical drive, another account's mapping would save fine and then fail at every
connect. The exception is a letter mounted **from the share being described**, which
`INasConnector.IsMountedFrom` answers: on Windows by comparing what `WNetGetConnection`
reports the letter points at with `\\host\share` under either spelling of the host, on
macOS by the volume being under Helix's own mount root. That is the normal state of a
machine a backup is restored on — persistent mappings survive a reinstall and live ones
survive an update, so the drives are up before the records that describe them are back —
and treating them as taken meant a vault of thirteen drives imported nothing until every
share had been disconnected by hand. A vault that ends up importing nothing is reported as
`JsonErrors.NothingToImport` rather than announced as a success.


### Finding a drive, and a row in the log

Both lists filter live, from a box that is always on screen, over what is already in
memory. Both used to be a modal: open a sheet, type, press Search, wait for a database
round trip, and have the results replace the list — four interactions and a query to narrow
thirteen rows. `SearchDrives` and `SearchAuditlogs` were deleted along with the sheets;
`GetDrives` and `GetAuditlogs` already load everything, so the queries were narrowing a set
the app was holding anyway.

A drive letter is `GeneralValidation.IsDriveLetter`: one character, A to Z. `char.IsLetter`
let a hand-edited vault save `É` as a letter the edit modal could not even offer. Export and
import catch `IOException` — the folder picked is as likely as not a NAS share — and a vault
that adds nothing is `JsonErrors.NothingToImport`, not a success banner. EF Core's command
logging is filtered to Warning in `AddInfrastructure`, because every query at Information
rolled the 2 MB log over the reconnect lines it exists for.

Filtering in the viewmodel buys two things the queries could not have. The drive rows are
**reused** rather than rebuilt, so a tick, a mount in flight and a drive's offline reason
all survive typing — re-projecting fresh `DriveDisplay`s per keystroke would have thrown the
selection away on the first letter. And the audit log is matched against the **sentence the
row shows**, which is composed at display time in the user's language and does not exist in
the database: searching for "disconnected" used to find nothing while the page was full of
the word.

Each page keeps a master list (`_allDrives`, `_allAuditlogs`) and `Drives`/`Auditlogs` is
the filtered, sorted view onto it. **Anything that describes the estate rather than the
list reads the master**: the storage and connection tiles, the connectivity donut, group
membership, and the count "disconnect all" confirms against. A search box narrowing the
list below them is not the NAS getting smaller.

Rows filtered out are **deselected** on the way. Acting on a ticked row that is not on
screen is the one outcome worth ruling out — "disconnect" has to mean the rows the user can
see.

An empty list now has two states. Filtering thirteen drives down to none and being told
"you have no drives yet — add one" is the app forgetting what the user just typed, so
`ShowNoMatches` offers to clear the search and `ShowNoDrives` offers to add a drive.

Column headers sort. The list always drew a header row and never let anyone click it; the
only ordering was ascending or descending, chosen in the modal, with no way to say by what.
Letter, Name and Status sort; **storage usage does not**, because the column holds a
sentence built for reading ("1.2 TB free of 43.2 TB") and ordering drives by the text of
that puts 9 GB after 40 TB. A fresh column starts ascending rather than keeping the previous
direction, and the caret marks which column is live.

### The drive card at an ordinary window size

The screenshots that exposed this were taken at 150% display scaling, which is worth
remembering before reading pixel positions off one: a 1960px-wide window is a 1300 DIP
window, and after the sidebar and the 320 DIP connectivity column the drive card is about
670 DIP wide. Fixed row columns totalling 636 left the one flexible column — the drive's
**name** — with nothing, and the header's title, search box and three labelled chips wanted
about 810 in a 610 slot, so the search box got squeezed to a stub.

`HomeViewModel.IsCompact` is the answer, set by the page from the **card's own measured
width** (`DriveCard.SizeChanged`, threshold `CompactCardWidth`), not the window's — the
card is what has to fit. Compact hides the chip labels (each chip carries the same text as a
tooltip, so icon-only is still explained) and collapses the storage-usage column to 0.
Storage is the column that goes because for every drive that is down it reads "Drive not
ready", and the name is the column that matters.

The column width lives on `Common/DriveListLayout`, an observable singleton that the header
grid and every row grid bind with a **static `Source`** — never `RelativeSource`. A
`ColumnDefinition` is a `BindableObject` but not an `Element`: it has no place in the visual
tree and no parent, so an `AncestorType` lookup on it has nothing to walk and fails inside
the item template, which took every row with it — a list that counted thirteen drives and
drew none. A static-source binding needs only something to subscribe to and works on any
bindable object. `HomeViewModel.IsCompact` mirrors into the singleton for the labels that
bind through the viewmodel.

**The header grid on `HomePage` and the row grid in `DriveTemplate` must stay identical**
— same widths, same spacing — or the column labels drift off the data. The twelve-DIP
column gap beside the collapsed storage column is the one thing the binding cannot remove,
and is accepted.

### Long lists

Both lists are `CollectionView`, not `BindableLayout` over a `VerticalStackLayout` in a
`ScrollView`. That shape realizes every row before the page can be drawn, which is
survivable for thirteen drives and is not for ninety days of audit history — the default
retention. `SelectionMode` is `None` on both: the drive rows carry their own tick, and
CollectionView's own selection would also fire on a click anywhere in the row, so pressing
"Disconnect" would select the row as a side effect.

Rows are recycled, which `DriveTemplate` already handles — `OnBindingContextChanged`
unregisters before it re-registers, so a view handed a different drive does not keep the
old one's subscriptions.

**The rows are still mouse-only.** Ctrl+F focuses the drive filter and that is the extent of
the keyboard story. Making a row itself keyboard-operable is not a small change: its status
pills and icon chips are styled `Border`s with tap gestures, which take no focus, and MAUI's
`Button` takes text rather than arbitrary content — so every interactive part of the row
would have to be rebuilt against a focusable control with a custom template before arrow
keys and Enter could reach it. Worth doing; not a detail.

The other thing left undone: `GetAuditlogs` still loads the whole history in one go.
Virtualizing the rows means the page no longer *draws* thousands of them, but it still reads
them all out of SQLite and holds them. Paging that query is the real fix, and it is
untouched.

### Installing an update

`GitHubUpdateChecker` also reads the release's `assets` and picks the archive whose name
carries a moniker this machine can run — `win-x64`, `win-arm64`, `macos`, matching what the
release workflow publishes. The match is on `-{moniker}.` and has to stay exact enough that
an x64 machine can never be handed the arm64 build, which installs cleanly and then will
not start. A release with no asset for this machine still reports the update; only the
release page is offered.

`UpdateConfiguration.AssetMonikers` asks the **operating system** its architecture, not
this process, and returns candidates best-first. What gets replaced is the whole install
folder, which the helper then starts fresh, so the question is what the machine can run
rather than what happens to be running: a 32-bit build on 64-bit Windows reported x86, had
no asset, and could never update itself out of that state. Arm64 lists `win-x64` behind
`win-arm64` because Windows on Arm runs x64 under emulation, so a release carrying no Arm64
archive is still installable; the fallback only ever runs in that direction, and
`SelectAsset` walks the list in order rather than matching it all at once, which is the
whole of what keeps that safe.

`UpdateInstaller` splits the work at the point it stops being undoable. `StageAsync`
downloads, checks the bytes against the digest GitHub published, unpacks, and checks that
what came down contains `Helix.App.exe` (or a `.app` bundle), all while Helix runs and with
the install untouched — every failure before this point costs nothing, and none of them
leave the download behind. `Apply` writes a helper script, starts it and returns; the caller
**must quit immediately**, because the helper is waiting on this process to exit before it
moves anything. The helper moves the install aside rather than writing over it, so a
failure halfway puts back exactly what was there; the moved-aside copy is deleted only once
the copy has finished.

The helper is started with its **`WorkingDirectory` set to its own folder**, and that line
is load-bearing on Windows. A child process with no working directory set inherits the
parent's, which for Helix is the install folder — Explorer starts an app there, and
`WindowsStartupService` and `WindowsDesktopService` both set it there explicitly in the
shortcuts they write. A process holds a handle to its current directory and Windows will
not rename a held folder, so the move that puts the install aside failed with a sharing
violation every time, the script's `catch` swallowed it, and the last line started the old
build again: 2.2.0 "updated" to 2.2.1, restarted, and was still 2.2.0. PowerShell's
`Set-Location` inside the script does not fix this — it moves the shell's location, not the
process's current directory, and it is the process handle that holds the folder.

The move is retried for `MoveAttempts` seconds, because an indexer or a virus scanner
holding the folder for a moment should not cost the release.

The helper replaces the **whole folder** the executable runs from, and the release zip has
no wrapper folder, so "extract here" in Downloads puts `Helix.App.exe` straight into
Downloads. `IsSafeToReplace` refuses to stage or apply from a shell folder, a drive root, a
folder without the executable, or anywhere under the staging root, with
`UpdateErrors.UnsafeInstallLocation`; without it the swap would have moved Downloads aside
and deleted it. The helper also **gives up if Helix is still running** after its wait,
rather than starting a second copy beside the first; restarts Helix with
`-WorkingDirectory $install` so the new process does not inherit the helper's folder; and,
when a restore finds the install folder still standing because a half-copied file is
held, copies the backup's contents back over it rather than `Move-Item`, which would have
put the backup *inside* the broken install. A stale `.old` that cannot be removed is
logged and the update skipped, so the script never dies before the restart line. Only
assets ending in `.zip` are considered, so a future `.sha256` sidecar cannot be staged as
the build.

`MauiProgram` holds a named mutex per logon session and a second instance brings the
first's window forward and exits: two Helixes on one database is what the helper
produced, and what a shortcut double-clicked while the first is in the tray produces. Whatever happens is appended
to `helix-updates.log` in the log directory, named so `LogFileWriter` collects it into the
diagnostics zip: the helper outlives the logger, and its failure path puts the old version
back and starts it, which is indistinguishable from success unless it is written down.

Neither script carries a comment of its own and both are built line by line rather than as
raw string literals. Each lives in a branch excluded on the other platform, and the
preprocessor still scans excluded regions for directives: any line starting with `#` — a
shell comment, a shebang — reads as one and fails the other head's build.

Nothing verifies a signature, because the release archives are unsigned and there is
nothing to verify against. What there is: TLS to github.com, the SHA-256 the release API
publishes for each asset, and the check that the archive holds the executable it claims to.
The digest is compared before the archive is opened, hashed as the download is written
rather than by reading the file back, and a mismatch is `UpdateErrors.DownloadCorrupt` —
distinct from an unreadable archive, because a truncated or substituted file may well open
perfectly well. It proves the bytes are the ones the API described, not who built them; the
signature is still the missing half. A release that publishes **no** digest, or one in an
algorithm this build does not know, is staged anyway: GitHub only began returning the field
recently, so refusing those would break updating for exactly the installs furthest behind,
and a hard failure on an unknown algorithm would let one change at GitHub's end stop every
install at once.

Staged downloads do not accumulate. `StageAsync` prunes every other release folder under
the staging root before it starts — they are installed or abandoned, and each is an
unpacked build of a couple of hundred megabytes — and the swap script deletes the folder it
copied out of, but only after the copy has succeeded, since until then it is the only copy
of the new version. The helper's own folder is never pruned: a helper started seconds ago
may still be running from it. `ReleaseFolderOf` finds what the helper should delete by
climbing to the child of the staging root rather than by taking the path apart, because the
payload sits a folder or two below it (`unpacked`, and on macOS the `.app` inside that);
anything not under the staging root at all yields nothing to delete, and both scripts guard
on that before removing anything.

### The idle lock

`Settings.IdleLockMinutes` asks for the password again after that many minutes without
input; 0 never locks, and that is the default for new accounts and existing ones alike.

It is a **lock, not a sign-out**, and the distinction is the whole design. Signing out stops
`DriveWatchdog`, `TrayIconService` and `StorageAlertService`, because all three act as the
signed-in user. Locking stops none of them: the session stays live, the drives stay mounted
and the watchdog keeps reconnecting them behind the lock screen. An unattended NAS tool
that stopped working the moment nobody was at the keyboard would have it exactly backwards.
`UnlockSession` therefore only reads `ILoggedInUser` — it establishes nothing.

`IIdleTimeProvider` reports **system-wide** idle time, not this app's: someone working in
another window is at their desk. Where it cannot tell, it answers zero — "someone is here" —
because a lock nobody asked for is worse than one that failed to happen.

`LockPage` is its own Shell route, and `AppShell.OnNavigated` groups it with the sign-in
pages when it disables the flyout. A locked session that still showed the sidebar would be
one anyone could click straight past, into the drive list it was put up to cover.

### The audit log

`Auditlog` stores an `AuditAction` plus the drive's id, name and letter **as they were at
the time** — never a composed sentence. The sentence is built in `AuditlogDisplay` from
localized format strings, so the page reads in the user's language and a later rename
cannot rewrite history. `AuditAction.Legacy` marks rows written before this and renders
their stored `Message` verbatim.

`InsertAuditLogsInterceptor` deliberately skips a save whose only modified properties are
`LastConnectedOnUtc` and `ModifiedOnUtc`: connecting a drive stamps it, and without that
filter every connect would file a "the drive was changed" entry.

### Helix.Infrastructure

Concrete implementations, one folder per abstraction group:

```
Authentication/  Connector/  Cryptography/  Desktop/  Diagnostics/  Platform/
Startup/  Storage/  Time/  Updates/
Database/
    AppDbContext.cs, AppDbContextFactory.cs
    Configurations/   EF entity configurations
    Constants/        table names, connection settings
    Interceptors/     InsertAuditLogsInterceptor
    Repositories/     the IXxxRepository implementations
    Sqlite/           SQLCipher command-builder plumbing
Migrations/
DependencyInjection.cs
```

#### Platform seams

Exactly six abstractions have a genuinely per-OS implementation, and they are bound in
`AddPlatformServices()` behind `#if WINDOWS` / `#elif MACCATALYST` (with an `#else` that
throws, so a new head fails at composition rather than at first use):

| Abstraction | Windows | macOS |
|---|---|---|
| `INasConnector` | `WindowsNasConnector` — `mpr.dll` WNet, mounts to `Z:` | `MacNasConnector` — `NetFSMountURLSync`, mounts to `~/Helix Drives/Z` |
| `IStartupService` | `WindowsStartupService` — `.lnk` in the Startup folder | `MacStartupService` — LaunchAgent plist |
| `IDesktopService` | `WindowsDesktopService` — `.lnk` on the Desktop | `MacDesktopService` — symlink to the `.app` |
| `ITrayIcon` | `WindowsTrayIcon` — `Shell_NotifyIcon`, hidden window on its own message loop | `UnsupportedTrayIcon` — no-op, `IsSupported` is false |
| `IStorageProbe` | `WindowsStorageProbe` — mounts are `Z:\` | `MacStorageProbe` — mounts are `~/Helix Drives/Z` |
| `IIdleTimeProvider` | `WindowsIdleTimeProvider` — `GetLastInputInfo` | `MacIdleTimeProvider` — `CGEventSourceSecondsSinceLastEventType` |

`INasConnector.GetMountPath` is the seventh thing that needs to know where a letter
lives, after the two connectors and the two probes. It is asked of the connector because
the connector is what put it there; it answers for any letter, mounted or not.

Opening that folder goes through `IFileBrowser`, which is deliberately **not** a seventh
platform seam: `ProcessStartInfo.UseShellExecute` hands a directory to Explorer on Windows
and to `open` on macOS, so one implementation covers both and it is registered outside
`AddPlatformServices()`. The drive row's folder button is the whole reason it exists — the
point of the app is that `Z:` is there, and until this there was no way to go and look at
it.

Both storage probes derive from `StorageProbe`, which holds the part that matters: **one
reading per volume, not per drive**. Several mapped drives are usually several shares of
one NAS pool, and each reports that pool's entire size, so adding the letters up read a
43.2 TB QNAP mapped thirteen times as 562 TB.

Two mounts are treated as one volume when they report the **same total size, to the byte**.
That is a property of the filesystem: it is identical across every share of a pool and it
does not move while the probe runs. Three other ideas were tried against that real
thirteen-share NAS and all failed — do not reintroduce them:

- **The volume serial number** (`GetVolumeInformation`). Looks precise, is not, for SMB:
  Samba and most NAS firmware derive it per share, so thirteen shares gave thirteen
  serials and nothing merged. Volume labels are per-share for the same reason.
- **The free byte count**, as part of the key. Free space drifts continuously on a NAS
  anything is writing to — across those thirteen shares it spanned ~12 MB with no two
  readings equal — so requiring it to match merged nothing either.
- **The host**, as part of the key. It splits one NAS added twice under two spellings (by
  IP and by name), and that fails in the damaging direction: over-counting.

The accepted cost is that two volumes of byte-identical size are counted once. Real volume
sizes are not round numbers, so that means two identically built volumes, and it
understates rather than multiplies. The smallest free reading in a group is the one kept,
so the figure does not flicker as parallel probes finish in a different order.

Do not "simplify" the dashboard total back into a sum over drive letters.

Both connectors take the NAS password as a separate credential argument rather than
putting it in a command line — do not "simplify" either into a `net.exe` or
`mount_smbfs //user:pass@host` shell-out.

Windows allows **one credential context per server for the whole logon session**, which is
why `WindowsNasConnector` does three things it would otherwise not need to.

It holds a per-host `SemaphoreSlim` around each mount, so the thirteen shares of one NAS
that "connect all" and the drive groups hand over at once do not race to establish that
context and come back `ERROR_SESSION_CREDENTIAL_CONFLICT` (1219). The gate's wait is
capped at the mount timeout, so an absent NAS cannot turn "connect all" into five seconds
per drive; going ahead ungated is safe there, because a server that is not answering has
no session to conflict over.

When a mount hits 1219 anyway, **what happens next turns entirely on whether any drive
letter is still mounted from that server** (`HasLiveConnectionTo`), and the two branches
must not be collapsed into one:

- **Something is using it.** A share mapped by hand in Explorer, or the first of this
  NAS's own thirteen drives. Windows will not hold a second credential set for that
  server while the first is in use, so the mount is retried **with no credentials at
  all** — how a share is asked onto an existing session — and comes up on whatever
  account that session belongs to. This is the branch that stopped one drive working and
  twelve failing.
- **Nothing is using it.** The context is a leftover: a session outlives the last
  connection to it, invisible to `net use` and unrelated to Credential Manager, and the
  deviceless session `Test` opens is a common way to leave one behind. Joining it would
  mount the share while proving nothing about the credentials, so **a wrong password
  would go on working** until the leftover expired. The session is dropped instead
  (`DropIdleServerSession`) and the drive's own credentials get a real attempt, whose
  error — a plain logon failure, usually — is what gets reported.

`HasLiveConnectionTo` can only answer for sessions that carry a **drive letter**, and a
deviceless one — a backup client that mounts the NAS at boot, an Explorer window left on a
UNC path, a leftover of Helix's own `Test` — is indistinguishable from nothing at all. So
the drop is refused by whatever holds it and the honest retry conflicts a second time.
**That second conflict is the tell**: a real leftover would have gone. The session is
therefore treated as the first branch after all and joined with no credentials, rather than
the drive giving up. Every drive of a NAS failing at boot because a backup had opened it
first is what the missing third case cost. Only a retry that comes back with something
*other* than 1219 — a logon failure, a missing share — is reported as the failure it is.

The wrong-password guarantee is untouched by that, because it only ever applied to the
genuinely idle case: there the drop succeeds and the retry is a real authentication. A
session that refuses to be dropped was never going to test a password.

`Test` takes the same first branch for the same reason, and more urgently: a connection
test answered out of a session nobody is using has tested nothing. It deliberately does
**not** gain the join-it-anyway fallback — a test that mounts on somebody else's session
has tested nothing either, so a conflict it cannot clear is reported as a conflict.

Dropping the session is only ever done on the second branch. On the first,
`WNetCancelConnection2` against a server name would take down the user's Explorer
mappings and every Helix drive already mounted on that NAS — never do that from a
background reconnect. Even on the second branch the cancel is **unforced**, so a
deviceless connection held by some other process refuses it rather than being broken.

When even the join is refused, one thing is left: Windows keys credentials per server
**name string**, not per machine, so `\192.168.1.6` and `\NAS` are two slots for one NAS.
`TryAlternateSpelling` asks `HostSpelling` for the other spelling and mounts under it,
where there is no session to conflict with and the drive's own credentials get a clean
attempt. It is a last resort and must stay one: its cost is a second credential context to
a NAS that already has one, which is the state that produces these conflicts. A failure
there returns null rather than an error, so what gets reported is still the conflict that
actually stopped the drive.

`Drive.ConnectByHostname` is the same trick asked for in advance — mount under the server's
name rather than the address typed into `Host`, so the collision never happens. It is worth
more than the fallback, because it never joins anybody's session: the drive comes up on its
own credentials, so a wrong password is still reported as one. Off by default, a no-op
unless the host is an IP literal, and hidden on macOS (`DrivePlatform.SupportsHostnameConnect`)
where NetFS asks for credentials per mount and the spelling buys nothing.

`HostSpelling` caches every answer for the life of the process, **including the failures**:
a home router with no PTR records for its DHCP leases is the normal case, not the
exception, and a lookup per drive per sweep would put thirteen of them on the critical path
of every reconnect. Lookups are capped at 1.5s and abandoned rather than cancelled, because
the BCL's synchronous resolver takes no token. A lookup that answers nothing leaves the
address as typed — failing a mount because DNS was quiet would turn an optional improvement
into a new way to lose a drive.

That normal case reaches the BCL as an **exception**: `Dns.GetHostEntry` throws
`SocketException` for an address with no PTR record, and `GetHostAddresses` does the same
for a name that does not resolve. Both are caught where they are raised, so "nothing" is a
return value rather than something for the blanket `catch` in `Within` to mop up — which
matters now that the drive diagnosis calls this on the ordinary path rather than only on a
credential conflict. A lookup that outlives its 1.5s cap is abandoned, and its exception is
observed on the way past: an abandoned task that faults with nobody watching is an
`UnobservedTaskException` raised from the finalizer, minutes later, with no way to tell
what asked for it.

The limit worth knowing, because no amount of code moves it: while other shares of a NAS
are mounted, a drive on that NAS mounts on the existing session and its stored password
is never checked. Editing a password and reconnecting that one drive will appear to work
whatever you type. Only with nothing else mounted from that server does the password get
tested.

macOS has no drive letters, so `Drive.Letter` names a directory under the mount root
instead. The persisted domain model is identical on both platforms.

`Drive.Host` accepts an IPv4 address, an IPv6 address or a hostname, so each connector
renders it into its own platform's form: Windows encodes IPv6 as `ipv6-literal.net`
because a UNC path cannot contain a colon, macOS brackets it for the `smb://` URL.
`Drive.Persistent` is Windows-only — it selects `CONNECT_UPDATE_PROFILE` over
`CONNECT_TEMPORARY`, and the Mac connector ignores it because NetFS has no equivalent.

`ITrayIcon` renders labels and reports which one was clicked, and knows nothing about
drives. Deciding what the menu says and what a click means is `TrayIconService`'s job in
the presentation layer, which is the only layer that can open a DI scope and reach a use
case — the same split as `IDriveMonitor` and `DriveWatchdog`.

Windows implementations carry `[SupportedOSPlatform("windows")]` and compile on both
heads; macOS implementations are wrapped in `#if MACCATALYST` because they reference
Apple BCL types that only exist on that target framework. `Platform/MacBundle` resolves
the running `.app` bundle for the two shortcut services.

### Helix.App (presentation)

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
               Settings/, Users/
Models/        observable display models bound by the views
Platforms/     MAUI platform heads
Resources/     AppIcon, Fonts, Images, Languages, Splash, Styles
Services/      DriveWatchdog, TrayIconService, StorageAlertService, IdleLockService,
               ModalHost, PassphrasePromptService, Notifier
ViewModels/    BaseViewModel + Auditlogs/, Drives/, Settings/, Users/
Views/         pages, modals and item templates: Auditlogs/, Drives/, Settings/, Users/
```

A view and its viewmodel sit in the same feature folder under their respective roots — `Views/Drives/HomePage.xaml` pairs with `ViewModels/Drives/HomeViewModel.cs`.

`GlobalUsings.cs` imports `Helix.App.Common` and `Helix.App.Localization` alongside the SharedKernel namespaces, so `ScopedHandler`, `PageNames` and `LocalizationResourceManager` need no per-file using.

#### What the dashboard gives room to

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

#### Numbers in the settings page

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

Only these four confirm themselves, through `Notifier`. A switch is its own confirmation —
it is sitting there in its new position — but a number typed into a box looks identical
whether it was stored or thrown away, and the write lands well after the keystroke that
caused it.

#### Platform-specific presentation code

The XAML, viewmodels, converters and behaviours are shared verbatim; only these carry an
`#if`, and each has a working macOS path or a deliberate no-op:

- `MauiProgram` — the WinUI lifecycle hook and the `EntryHandler` chrome tweak are
  `#if WINDOWS`. The SharpHook startup is not: as of SharpHook 8 the package ships a Mac
  Catalyst assembly and the libuiohook natives for it, so the global hook runs on both
  heads.
- `App.CreateWindow` — Catalyst sizes its window here from `Common/WindowSizing`, the
  same rule the Windows lifecycle event applies through `AppWindow`. Change the rule in
  one place and both heads follow.
- `Common/MainWindow` — hides, minimizes and restores the app window; Windows only. Mac
  Catalyst exposes no public API for a window scene, so `BaseViewModel.MinimizeApp` runs
  its countdown and then does nothing on macOS rather than reaching for a private
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

#### XAML conventions

XML namespace prefixes are consistent across every page and modal:

- `vm:` — the file's viewmodel namespace (`x:DataType`)
- `views:` — sibling views in the same feature
- `l10n:` — `Helix.App.Localization`, for `{l10n:Translate Key}` and `LocalizationResourceManager`
- `controls:`, `behaviors:`, `converters:`, `icons:`, `models:` — the matching folders
- `local:` — the root `Helix.App` namespace (only `AppShell.xaml` needs it)

### Dependency injection

DI is composed via three static extension methods chained in `Helix.App/MauiProgram.cs`:

- `services.AddApplication()` — `Helix.Application/DependencyInjection.cs` registers every handler as `Scoped`. New handlers must be added here.
- `services.AddInfrastructure()` — `Helix.Infrastructure/DependencyInjection.cs` registers `AppDbContext`, repositories, auth, time, NAS connector, etc.
- `services.AddPresensation()` — `Helix.App/Extensions/DependencyInjection.cs` (note: the method name is misspelled but kept consistent across the codebase).

Handlers are scoped and must never be cached in viewmodel/page fields. The presentation layer invokes them per operation through `ScopedHandler.HandleAsync((MyHandler h) => h.Handle(request))` (`src/Helix.App/Common/ScopedHandler.cs`), which creates a DI scope per call so each operation gets a fresh `AppDbContext`. Only singletons (`ILoggedInUser`, `INasConnector`, `IDriveMonitor`, `ICountdownService`, `IGlobalHook`, `IVaultCipher`, `IDateTimeProvider`) may be resolved from `App.ServiceProvider` and stored in fields.

### Persistence

`AppDbContext` (`src/Helix.Infrastructure/Database/AppDbContext.cs`) implements both `IDbContext` and `IUnitOfWork` (abstractions in `Helix.Application/Abstractions/Data/`). The SQLite database is encrypted: the connection string is built with a password from `PasswordGenerator.GetOrCreatePassword()`, and `IRelationalCommandBuilderFactory` is replaced with a custom builder (`Database/Sqlite/CustomRelationalCommandBuilderFactory`) to support the cipher. `InsertAuditLogsInterceptor` is registered as a singleton and attached to the context to write audit logs automatically on save. Entity configurations are picked up via `ApplyConfigurationsFromAssembly` from `Database/Configurations/`.

The `DbContext` lifetime and threading were a recurring issue historically; the fix is the per-operation scope pattern above (`ScopedHandler`). Do not resolve `AppDbContext` (or anything scoped) from the root provider, and do not share a context instance across concurrent operations.

#### Where the data lives

`DatabaseLocation` is the one place that knows: `helix.db` in `FileSystem.AppDataDirectory`,
with the SQLCipher key beside it in MAUI's `Settings/securestorage.dat`. On an unpackaged
Windows build that directory is `%LOCALAPPDATA%\<publisher>\<package>\Data`, and **both
names come out of the build**: the AppInfo metadata the resizetizer stamps from a
`Package.appxmanifest` when the project has one, and the assembly's company and title
(`Helix.App`) when it does not. The release workflow builds from a checkout with no
manifest — `Platforms/Windows/Package.appxmanifest` is gitignored — so every published
build reads and writes `%LOCALAPPDATA%\Helix.App\Helix.App\Data`. A machine that builds
with a manifest in place resolves to whatever that manifest says; this one produced
`%LOCALAPPDATA%\YourName\com.companyname.helix.app\Data` until late August 2026, and both
folders are still there. A copy of the app built that way keeps its account and drives in a
folder the published builds never look in, so updating it to a release comes up with an
empty database and the register page, while the Windows mappings it made are still up.
**Do not add a manifest, or a `Company`/`AssemblyTitle`, without deciding what happens to
the data in the old folder.**

`DatabaseInitializer` runs the migrations at startup and logs, at Information, the
directory it used and whether a database was there — the one line the diagnostics zip was
missing when a user reported exactly that. `PasswordGenerator` logs when it has to
generate a key rather than read one, since a fresh key on a machine that already has a
database is what makes that database unreadable. Before a migration runs against an
existing database, a copy is left beside it as `helix.db.bak`, overwritten by the next one.

`PasswordGenerator` **never generates a key while `helix.db` exists.** A read that threw
used to be indistinguishable from "no key", so one DPAPI failure at logon generated a
fresh key, wrote it over the real one, and the database was unreadable for good even
after the failure cleared. It now throws, and both that and a database that will not
open or migrate go through `Common/StartupFailure`, which logs at Critical, puts up one
native message box naming the reason and the log folder, and exits. There is no window
yet at either point, so nothing else could have said anything.


### Localization

UI strings live in `src/Helix.App/Resources/Languages/AppResources.resx` with translations for `de`, `fr`, `id`, `ja`, `nl`. Use `TranslateExtension` (the `{l10n:Translate}` XAML markup extension) and `LocalizationResourceManager` rather than hard-coding strings.

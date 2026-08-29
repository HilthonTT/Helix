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
Core/           Errors, Sorting, Validation — cross-feature helpers
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

`DriveWatchdog` reads that error code and puts the drive back at a flat 30-second interval
without counting it as a failure, so a laptop returning to the NAS's network reconnects in
seconds rather than at whatever the exponential backoff had escalated to. It logs those at
Debug rather than Warning — on a laptop this is the ordinary state of affairs for most of
the day, and a warning per drive per sweep buries the real failures in the file the user is
asked to send on. The sweep is skipped outright when `Connectivity` reports no network.

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
A volume that recovers is forgotten, so it can warn again months later. It starts a minute
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
holding the folder for a moment should not cost the release. Whatever happens is appended
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

`Test` takes the same branch for the same reason, and more urgently: a connection test
answered out of a session nobody is using has tested nothing.

Dropping the session is only ever done on the second branch. On the first,
`WNetCancelConnection2` against a server name would take down the user's Explorer
mappings and every Helix drive already mounted on that NAS — never do that from a
background reconnect. Even on the second branch the cancel is **unforced**, so a
deviceless connection held by some other process refuses it rather than being broken.

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
Controls/      custom controls and layouts (NavItem, ChartView, HorizontalWrapLayout)
Converters/    IValueConverter implementations
Extensions/    DependencyInjection (AddPresensation)
Icons/         IconFont glyph constants
Localization/  LocalizationResourceManager, TranslateExtension, CultureSwitcher
Messaging/     CommunityToolkit.Mvvm messages, by feature
               Auditlogs/, DriveGroups/, Drives/, Navigation/, Settings/, Users/
Models/        observable display models bound by the views
Platforms/     MAUI platform heads
Resources/     AppIcon, Fonts, Images, Languages, Splash, Styles
Services/      DriveWatchdog, TrayIconService, StorageAlertService, IdleLockService,
               ModalHost, PassphrasePromptService
ViewModels/    BaseViewModel + Auditlogs/, Drives/, Settings/, Users/
Views/         pages, modals and item templates: Auditlogs/, Drives/, Settings/, Users/
```

A view and its viewmodel sit in the same feature folder under their respective roots — `Views/Drives/HomePage.xaml` pairs with `ViewModels/Drives/HomeViewModel.cs`.

`GlobalUsings.cs` imports `Helix.App.Common` and `Helix.App.Localization` alongside the SharedKernel namespaces, so `ScopedHandler`, `PageNames` and `LocalizationResourceManager` need no per-file using.

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

### Localization

UI strings live in `src/Helix.App/Resources/Languages/AppResources.resx` with translations for `de`, `fr`, `id`, `ja`, `nl`. Use `TranslateExtension` (the `{l10n:Translate}` XAML markup extension) and `LocalizationResourceManager` rather than hard-coding strings.

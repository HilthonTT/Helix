# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Helix is a .NET MAUI desktop app for managing connections to NAS drives. It ships two heads:

- **Windows** — `net10.0-windows10.0.19041.0`. Open `Helix.sln` in Visual Studio 2022 to build/run.
- **macOS** — `net10.0-maccatalyst`. Build on a Mac with Xcode installed; Catalyst cannot be compiled from Windows.

`Helix.App` and `Helix.Infrastructure` each gate their target frameworks on the host OS, so `dotnet build` produces the right head on either machine without a `-f` switch. Adding a target framework to one of those projects means adding it to both.

## Commands

Build and test from the repo root:

```bash
dotnet build Helix.sln
dotnet test Helix.sln
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

Entities, domain errors and repository **interfaces**, one folder per aggregate (`Auditlogs/`, `Drives/`, `Settings/`, `Users/`). Also framework-free: no MAUI, no EF Core, no Application/Infrastructure dependency. Error classes are plural (`DriveErrors`, `UserErrors`, `SettingsErrors`, `AuthenticationErrors`).

### Helix.Application

```
Abstractions/   Authentication, Connector, Cryptography, Data, Desktop, Diagnostics,
                Handlers, Security, Startup, Storage, Time, Updates — interfaces only
Core/           Errors, Sorting, Validation — cross-feature helpers
Features/       one folder per feature, split into Commands / Queries
                Auditlogs/{Commands,Queries}
                Diagnostics/Commands
                Drives/{Commands,Queries,Contracts}
                Settings/{Commands,Queries}
                Updates/Queries
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
is `2.0` while releases are tagged `v2.0.0`, and `Version` treats a missing component as
**-1, not 0** — so an unnormalized compare makes the running build "older" than the release
it was built from and announces an update to itself. Both sides are widened to four
components first. A tag that is not a version (`nightly`) is refused rather than guessed at.

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

Exactly five abstractions have a genuinely per-OS implementation, and they are bound in
`AddPlatformServices()` behind `#if WINDOWS` / `#elif MACCATALYST` (with an `#else` that
throws, so a new head fails at composition rather than at first use):

| Abstraction | Windows | macOS |
|---|---|---|
| `INasConnector` | `WindowsNasConnector` — `mpr.dll` WNet, mounts to `Z:` | `MacNasConnector` — `NetFSMountURLSync`, mounts to `~/Helix Drives/Z` |
| `IStartupService` | `WindowsStartupService` — `.lnk` in the Startup folder | `MacStartupService` — LaunchAgent plist |
| `IDesktopService` | `WindowsDesktopService` — `.lnk` on the Desktop | `MacDesktopService` — symlink to the `.app` |
| `ITrayIcon` | `WindowsTrayIcon` — `Shell_NotifyIcon`, hidden window on its own message loop | `UnsupportedTrayIcon` — no-op, `IsSupported` is false |
| `IStorageProbe` | `WindowsStorageProbe` — mounts are `Z:\` | `MacStorageProbe` — mounts are `~/Helix Drives/Z` |

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
               Auditlogs/, Drives/, Navigation/, Users/
Models/        observable display models bound by the views
Platforms/     MAUI platform heads
Resources/     AppIcon, Fonts, Images, Languages, Splash, Styles
Services/      DriveWatchdog, TrayIconService, ModalHost, PassphrasePromptService
ViewModels/    BaseViewModel + Auditlogs/, Drives/, Settings/, Users/
Views/         pages, modals and item templates: Auditlogs/, Drives/, Settings/, Users/
```

A view and its viewmodel sit in the same feature folder under their respective roots — `Views/Drives/HomePage.xaml` pairs with `ViewModels/Drives/HomeViewModel.cs`.

`GlobalUsings.cs` imports `Helix.App.Common` and `Helix.App.Localization` alongside the SharedKernel namespaces, so `ScopedHandler`, `PageNames` and `LocalizationResourceManager` need no per-file using.

#### Platform-specific presentation code

The XAML, viewmodels, converters and behaviours are shared verbatim; only these carry an
`#if`, and each has a working macOS path or a deliberate no-op:

- `MauiProgram` — the WinUI lifecycle hook, the `EntryHandler` chrome tweak and the
  SharpHook startup are `#if WINDOWS`.
- `App.CreateWindow` — Catalyst sizes its window here from `Common/WindowSizing`, the
  same rule the Windows lifecycle event applies through `AppWindow`. Change the rule in
  one place and both heads follow.
- `Common/MainWindow` — hides, minimizes and restores the app window; Windows only. Mac
  Catalyst exposes no public API for a window scene, so `BaseViewModel.MinimizeApp` runs
  its countdown and then does nothing on macOS rather than reaching for a private
  selector. On Windows it hides to the tray while `TrayIconService` is running, because
  the icon is then the way back, and falls back to a plain minimize when it is not.
- `Common/DrivePlatform` — the one flag the shared drive modals bind to, so the
  "reconnect at sign-in" switch is hidden rather than shown-and-ignored on macOS.
- `Behaviors/Hover` (hand cursor), `Services/ModalHost` (Escape-to-dismiss) and the
  `LoginPage`/`RegisterPage` Ctrl+Enter shortcut — Windows only, no-ops elsewhere.

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

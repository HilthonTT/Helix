# CLAUDE.md

Guidance for Claude Code in this repository. This file holds the rules; the reasoning behind
them lives in `docs/design/` (see **Design docs** at the end). **Read the matching design doc
before changing an area** — most rules below exist because the obvious version was tried and
broke on a real NAS.

## Project

Helix is a .NET MAUI desktop app for managing connections to NAS drives. Two heads:

- **Windows** — `net10.0-windows10.0.19041.0`. Open `Helix.slnx` in Visual Studio 2022; run `Helix.App` from there, not `dotnet run`.
- **macOS** — `net10.0-maccatalyst`. Build on a Mac with Xcode; Catalyst cannot be compiled from Windows.

`Helix.App` and `Helix.Infrastructure` gate their target frameworks on the host OS, so
`dotnet build` picks the right head without `-f`. Adding a target framework to one means adding
it to both.

## Commands

```bash
dotnet build Helix.slnx
dotnet test Helix.slnx
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~CreateDriveTests"
dotnet ef migrations add <Name> --project src/Helix.Infrastructure --startup-project src/Helix.App --framework net10.0-windows10.0.19041.0
```

`scripts/` wraps these in PowerShell (5.1 or 7, each with `Get-Help`) and picks the right
projects for the host OS:

```powershell
./scripts/build.ps1 [-Configuration Release] [-Clean]
./scripts/test.ps1 [-Suite Application,Architecture] [-Filter CreateDriveTests] [-NoBuild] [-Trx]
./scripts/clean.ps1
./scripts/publish.ps1 [-Runtime win-x64,win-arm64] [-Zip] [-Tag v2.2.6]
./scripts/migration.ps1 add <Name> | remove | list
```

- `Application.UnitTests` targets plain `net10.0` and runs anywhere. `Infrastructure.UnitTests`
  and `ArchitectureTests` target the Windows head. On a Mac: build `src/Helix.App/Helix.App.csproj`
  and run only the Application suite.
- Test projects use **`xunit.v3.mtp-off`** (VSTest, so `--filter` and `--logger trx` work). Plain
  `xunit.v3` turns on MTP and the .NET 10 SDK then refuses VSTest. `xUnit1051` is suppressed.
- `Infrastructure.UnitTests` and `ArchitectureTests` set `WindowsAppSdkBootstrapInitialize=false`;
  without it the Windows App SDK bootstrapper hangs the test executable on CI runners.
- CI: Windows runs all three suites; macOS compile-verifies Catalyst and runs the Application
  suite. The macOS jobs pin the MAUI workload to **10.0.401** (10.0.401.1 needs Xcode 27, which
  `macos-latest` lacks). Lift the pin once the image carries it.

## Documentation website

`website/` is a Next.js static export: MDX under `app/docs/<page>/page.mdx`, sidebar order in
`lib/docs.ts`. `.github/workflows/website.yml` deploys `main` to GitHub Pages
(`NEXT_PUBLIC_BASE_PATH=/Helix`; Pages source must be GitHub Actions). `cd website && npm ci && npm run dev`.

Media in `website/public/media` is recorded from the real app by `website/scripts/record-media.py`,
which publishes with `-p:Company=HelixMedia -p:AssemblyTitle=HelixMedia` so it uses a separate
data folder, and uses RFC 5737 addresses. The README embeds those GIFs — re-record rather than
adding images elsewhere.

## Build configuration

- `Directory.Build.props` — shared properties (`ImplicitUsings`, `Nullable`).
- `Directory.Packages.props` — Central Package Management. `.csproj` files reference packages **without** `Version`.
- Keep project files free of anything those two already cover.
- **Adding a culture**: add it to `SatelliteResourceLanguages` in `Helix.App.csproj` **and** to
  `_HelixPruneWindowsAppSdkCultures` (which prunes WinUI's `.mui` folders). That target uses
  `String.Contains`, not a regex, on purpose — MSBuild mis-parses `(…)` in a quoted argument.
- **Versioning**: bump only `ApplicationDisplayVersion` in the `.csproj`. Leave
  `Platforms/Windows/Package.appxmanifest` on the `0.0.0.0` placeholder (a real number there
  wins and the app announces an update to itself). Do not remove `_HelixRefreshStampedAppxManifest`.
  Details in `docs/design/updates.md`.

## Code style

- **No comments in code** — no `///`, no `//`, in C# or XAML. Reasoning goes in `docs/design/`.
- **Namespaces mirror folders exactly** in every project (architecture tests enforce it for
  Infrastructure and Application handlers). Move the namespace with the file.
- Never `Debug.WriteLine`; use `ILogger<T>` (DI types) or `AppLog.For<T>()` (only pages,
  viewmodels and static helpers that MAUI rather than DI constructs). Release logs Information+.
  Keep credentials, hosts and share names out of log messages — users send these files to strangers.
- Every UI string is in `AppResources` (`en`, `de`, `fr`, `id`, `ja`, `nl`), via
  `{l10n:Translate Key}` / `LocalizationResourceManager`. Command-line replies are the one English exception.
- XAML prefixes: `vm:` (the file's viewmodel), `views:` (sibling views), `l10n:`, `controls:`,
  `behaviors:`, `converters:`, `icons:`, `models:`, `local:` (only `AppShell.xaml`).
- Everything a compiled binding reaches from a public host must be **public** (internal degrades to `XC0045`, a binding that silently does nothing).
- **No SVG under `Resources/` may use a `<filter>`** — breaks the Mac build (`MAUIR0001`). Use radial gradients.

## Architecture

Clean Architecture: SharedKernel → Domain → Application → Infrastructure / Presentation.
Dependencies point inward only; `tests/ArchitectureTests/Layers/LayerTests.cs` (NetArchTest)
enforces it. Each layer has an `XxxAssembly.cs` marker (`DomainAssembly`, `ApplicationAssembly`,
`InfrastructureAssembly`, `PresentationAssembly`).

- **SharedKernel** — `SharedKernel.Abstractions` (`IAuditable`, `IDateTimeProvider`),
  `.Primitives` (`Entity`, `Enumeration`, `Ensure`), `.Results` (`Result`, `Result<T>`, `Error`,
  `ErrorType`, `ValidationError`). Imported via each project's `GlobalUsings.cs`; files don't
  write `using SharedKernel...`. **No MAUI or EF Core reference.**
- **Helix.Domain** — entities, domain errors (plural: `DriveErrors`, `UserErrors`, …) and
  repository interfaces, one folder per aggregate. Framework-free.
- **Helix.Application** — `Abstractions/` (interfaces only), `Core/` (cross-feature helpers:
  Drives, Errors, Security, Sorting, Storage, Validation), `Features/<Feature>/{Commands,Queries,Contracts}`,
  `DependencyInjection.cs`. No Infrastructure dependency and **no MAUI**
  (`ApplicationLayer_Should_NotHaveDependencyOn_Maui`). Dialogs go through `IFolderPicker` /
  `IFilePicker` / `IPassphrasePrompt`, implemented in the presentation layer's `Services/`.
  (Those two names collide with MAUI/Toolkit types the App imports implicitly, so the
  implementations qualify one side.)
- **Helix.Infrastructure** — implementations, one folder per abstraction group, plus
  `Database/` (`AppDbContext`, `Configurations/`, `Constants/`, `Interceptors/`, `Repositories/`,
  `Sqlite/`) and `Migrations/`.
- **Helix.App** — MAUI presentation. A view and its viewmodel share a feature folder
  (`Views/Drives/HomePage.xaml` ↔ `ViewModels/Drives/HomeViewModel.cs`). `GlobalUsings.cs` also
  imports `Helix.App.Common` and `Helix.App.Localization`.

### Handler pattern

Every use case is a `sealed class` implementing `IHandler` under `Features/`, registered as
Scoped in `Helix.Application/DependencyInjection.cs` (**add new handlers there**):

```csharp
public sealed class CreateDrive(IDriveRepository repo, IUnitOfWork uow, ...) : IHandler
{
    public sealed record Request(/* inputs */);

    public async Task<Result<Drive>> Handle(Request request, CancellationToken ct = default)
    {
        // validate (static Validate) → authorize (ILoggedInUser) → domain checks
        // → mutate → await uow.SaveChangesAsync(ct) → return Result
    }
}
```

Handlers never throw for expected failures; errors come from static error classes.

### Dependency injection and threading

- Composition in `MauiProgram.cs`: `AddApplication()`, `AddInfrastructure()`, `AddPresensation()` (misspelling is intentional and consistent).
- Handlers are scoped: **never cache them in fields**. Call through
  `ScopedHandler.HandleAsync((MyHandler h) => h.Handle(request))`, a fresh scope (and `AppDbContext`) per call.
  Never resolve `AppDbContext` or anything scoped from the root provider.
- Only singletons may be stored from `App.ServiceProvider`: `ILoggedInUser`, `INasConnector`,
  `IDriveMonitor`, `ICountdownService`, `IGlobalHook`, `IVaultCipher`, `IDateTimeProvider`,
  `INetworkLocation`, `IWakeOnLan`.
- SQLite's async methods complete synchronously, so handlers block the calling thread. Run
  heavy handlers (saves, sign-in PBKDF2, paging, refresh-on-message) via `Task.Run` at the call
  site, close sheets **before** sending change messages. **Do not wrap `ScopedHandler` itself in
  `Task.Run`** — `ImportDrives`, `ExportDrives`, `ExportDiagnostics` raise UI pickers.
- **Every background loop starts with `Task.Run`**, and marshals its own UI work (`Notifier`,
  `MainWindow`, `EstateStatus`, `PublishToUi`). Never rely on the context it was started from.

### Persistence

- `AppDbContext` implements `IDbContext` and `IUnitOfWork`. Configurations come from
  `Database/Configurations/`. `InsertAuditLogsInterceptor` writes audit rows on save and skips
  saves that only touch `LastConnectedOnUtc`/`ModifiedOnUtc`.
- The cipher is **SQLite3 Multiple Ciphers** with `cipher=sqlcipher&legacy=4` in a hand-built
  `file:` URI (escape only `%`, `?`, `#`). **Do not drop the parameters or change the cipher**
  without a rekeying migration; `SqlCipherCompatibilityTests` guards it.
- Data folder: `FileSystem.AppDataDirectory`, which derives from the build's company/title.
  Published builds use `%LOCALAPPDATA%\Helix.App\Helix.App\Data`. **Do not add a
  `Package.appxmanifest` to the repo, or a `Company`/`AssemblyTitle`, without migrating the old folder.**
- `PasswordGenerator` must **never generate a key while `helix.db` exists**; startup failures go
  through `Common/StartupFailure`.

## Rules by area

Each block is the short form; the linked doc has the why.

**User feedback** (`notifications.md`)
- Results go through `Notifier` banners. **Never reintroduce a `DisplayAlert` for a result.**
  Modals are only for questions (delete group, install update, passphrase, unmount confirmation)
  and documents (the diagnosis sheet).
- A failure line about a drive is `"{Letter}: {description}"` — that is the unit `Notifier.Retract` removes.
- Tray-originated actions report through the tray balloon, falling back to the banner.

**Mounting and the connectors** (`platform.md`, `connectivity.md`)
- Every handler that mounts or unmounts on purpose holds an `IDriveMonitor.Suppress` handle for
  the whole operation. **Add one to any new such handler.** `ReconnectDrive` deliberately does not.
- `IsLiveFrom` = "the drive is up"; `IsMountedFrom` = "the letter points at this share"
  (includes remembered mappings). `GetLiveLetters` vs `GetConnectedLetters` likewise. Pick
  deliberately — the split table is in `drives.md`.
- Connectors take the password as a separate credential argument. **Never shell out** to
  `net.exe` / `mount_smbfs` with credentials.
- Windows SMB 1219 handling has distinct branches (live session → join with no credentials;
  idle leftover → unforced drop and real retry; drop refused → join). **Do not collapse them**,
  and never cancel a connection by server name from a background reconnect.
- SMB-only logic (1219 branches, `TryAlternateSpelling`, `ConnectByHostname`, `HasOtherMountsOn`,
  share browsing, adopting mappings) is guarded by `Drive.Protocol`. Probe ports come from
  `DriveProtocols.ProbePortsFor`. **Do not add SFTP or folder mounts** this way.
- Which address a connect uses is decided only in `IDriveRouter`. Anything that cannot tell
  reads as **reachable / home**, never unreachable / away.
- Mount timeout is 30s and the per-host gate is held until the mount itself finishes.
- `HostSpelling` caches answers for the process, but a timed-out lookup for only one minute.

**Storage** (`platform.md`, `storage.md`)
- Volumes are grouped by **identical total size**. Do not key on serial number, free bytes or
  host, and **do not turn the dashboard total back into a sum over letters**.
- A probe that measured nothing while drives are connected is not displayed as zero.

**Updates** (`updates.md`)
- Compare versions only through `ReleaseVersion` (four-part normalized). `UpdateCheck.LatestVersion`
  and `CurrentVersion` are both display form (`2.3.5`, no `v`); the UI adds the `v`.
- Asset match is exact on `-{moniker}.` and `.zip`. **Never rename the signature manifest
  `Helix-<tag>-signatures.txt` to anything containing a moniker.**
- The update helper's `WorkingDirectory` must be its own folder. Publish a signed release before
  shipping a build that sets `SigningPublicKey`.
- Update scripts in C# are built line by line with no lines starting with `#` (the other head's preprocessor reads them).

**UI** (`presentation.md`, `lists.md`, `themes.md`)
- Both lists are `CollectionView` with `SelectionMode=None`. Anything describing the estate reads the master list, not the filtered view.
- The header grid on `HomePage` and the row grid in `DriveTemplate` **must stay identical**. Bind
  `ColumnDefinition` widths with a static `Source` (`DriveListLayout`), never `RelativeSource`.
- A `Picker` needs its `ItemsSource` before its `SelectedItem`.
- Theme colors are `{DynamicResource Token}` from `ThemePalette` (add new colors to every palette);
  never `AppThemeBinding` or `StaticResource`. The theme choice lives in MAUI `Preferences`.
- Logo paths are shared with `website/components/logo.tsx` and `public/logo.svg` — change both.
- Platform `#if`s in the presentation layer are listed in `presentation.md`; any new one needs a working macOS path or a deliberate no-op.

**Platform seams** (`platform.md`)
Exactly seven abstractions are per-OS, bound in `AddPlatformServices()` behind `#if WINDOWS` /
`#elif MACCATALYST` / `#else throw`: `INasConnector`, `IStartupService`, `IDesktopService`,
`ITrayIcon`, `IStorageProbe`, `IIdleTimeProvider`, `INetworkLocation`. `IFileBrowser`,
`IHostDiagnostics`, `IDriveRouter`, `IHostReachability` and `IWakeOnLan` are single
implementations registered outside it. Windows types carry `[SupportedOSPlatform("windows")]`;
macOS types are wrapped in `#if MACCATALYST`. The Catalyst head has the App Sandbox disabled.

**Data** (`drives.md`, `schedules.md`, `security.md`)
- `Auditlog` stores an `AuditAction` plus drive id/name/letter, never a sentence; sentences are built at display time.
- Vault import: an invalid optional field (MAC, away address) is dropped, not refused; groups travel by letters, schedules by group name.
- CSV export prefixes formula-leading fields with `'` and is UTF-8 with BOM.

## Design docs

| Doc | Read before touching |
|---|---|
| `docs/design/notifications.md` | logging, `Notifier`, banners, retracting |
| `docs/design/connectivity.md` | `ReconnectDrive`, watchdog, reachability, Wake-on-LAN, resume, suppression, home network, away address, late mounts |
| `docs/design/diagnostics.md` | `DiagnoseDrive`, reliability figures |
| `docs/design/drives.md` | groups, selections, import/vault, `IsMountedFrom` vs `IsLiveFrom`, share browsing, adopting, credentials, WebDAV/NFS |
| `docs/design/schedules.md` | `Schedule`, `RunDueSchedules` |
| `docs/design/storage.md` | low-space alerts, storage history, forecasts, Insights |
| `docs/design/lists.md` | filtering, sorting, compact card, keyboard rows, audit paging |
| `docs/design/updates.md` | update check, versioning, installer, signatures |
| `docs/design/security.md` | recovery key, idle lock, audit log, CSV export |
| `docs/design/platform.md` | Infrastructure layout, platform seams, storage probe, Windows SMB credential handling, macOS tray |
| `docs/design/presentation.md` | close-to-tray, sidebar, countdown, settings numbers, hotkeys, command line / `helix://`, platform `#if`s |
| `docs/design/persistence.md` | DI, threading, database cipher, data location |
| `docs/design/themes.md` | themes, logo, tray/taskbar icons |
| `docs/design/build-and-architecture.md` | build props, culture pruning, xunit/CI setup, layer rules, pickers |
| `docs/release-signing.md` | signing key generation and rotation |

When you change behaviour in an area, update its design doc with the reasoning — that is where
explanations go instead of code comments.

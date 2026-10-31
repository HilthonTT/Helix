# Dependency injection, threading and persistence

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Dependency injection

DI is composed via three static extension methods chained in `Helix.App/MauiProgram.cs`:

- `services.AddApplication()` — `Helix.Application/DependencyInjection.cs` registers every handler as `Scoped`. New handlers must be added here.
- `services.AddInfrastructure()` — `Helix.Infrastructure/DependencyInjection.cs` registers `AppDbContext`, repositories, auth, time, NAS connector, etc.
- `services.AddPresensation()` — `Helix.App/Extensions/DependencyInjection.cs` (note: the method name is misspelled but kept consistent across the codebase).

Handlers are scoped and must never be cached in viewmodel/page fields. The presentation layer invokes them per operation through `ScopedHandler.HandleAsync((MyHandler h) => h.Handle(request))` (`src/Helix.App/Common/ScopedHandler.cs`), which creates a DI scope per call so each operation gets a fresh `AppDbContext`. Only singletons (`ILoggedInUser`, `INasConnector`, `IDriveMonitor`, `ICountdownService`, `IGlobalHook`, `IVaultCipher`, `IDateTimeProvider`, `INetworkLocation`, `IWakeOnLan`) may be resolved from `App.ServiceProvider` and stored in fields.

`ScopedHandler` runs the handler on whatever thread calls it — the UI thread, from a
viewmodel — and Microsoft.Data.Sqlite has no real asynchronous I/O: its `…Async` methods
complete synchronously. So every query and every encrypted write a handler makes blocks the
window. Saving a drive froze its sheet for exactly that reason, and then again for everything
`DriveUpdatedMessage` set off, because `WeakReferenceMessenger.Send` runs every recipient
before it returns and the sheet only closed after it. `UpdateDriveViewModel` now saves through
`Task.Run`, closes the sheet, and only then announces the change; `DriveWatchdog` and
`TrayIconService` do their refresh-on-message on the thread pool. **Do not wrap
`ScopedHandler` itself in `Task.Run`**: `ImportDrives`, `ExportDrives` and `ExportDiagnostics`
raise pickers and prompts through presentation services that expect the UI thread.
The same applies to the **sign-in handlers** (`LoginUser`, `RegisterUser`, `UnlockSession`,
`ChangeUserPassword`, each a 600k-iteration PBKDF2), to `CreateDrive`/`CreateDrives` (which
close their sheet before announcing, like the edit sheet), to the dashboard chart's and the
sidebar's refresh-on-`CheckDrivesStatusMessage`, and to the audit log's paging, which a
search runs to the end of the history.

**Every background loop is started with `Task.Run`** — `DriveMonitor`, the watchdog's retry
sweep, `StorageAlertService`, `IdleLockService`, `CommandListener` and the tray's show
retry. They are started from `HomePage.OnAppearing` or `CreateMauiApp`, on the UI thread, and
an `await` on a `PeriodicTimer` resumes on the context it was started from: every tick of
every one of them — the monitor's poll and everything its `ConnectivityChanged` fans out to,
the watchdog's reconnects and their database writes, the resume nudge's host lookups, every
command-line request — ran on the UI thread, whatever `Task.Run` its callees used. Whatever
they touch in the UI is marshalled by the thing that touches it (`PublishToUi`, `Notifier`,
`MainWindow`, `EstateStatus`, `LockAsync`); a new loop must do the same rather than lean on
the context it happened to be started from. `DriveMonitor` also invokes each
`ConnectivityChanged` subscriber on its own and survives a poll that throws: a single
exception used to end the loop, and drops then went unnoticed until the next sign-in.

`StorageProbe` shares a measurement that is still running for a letter rather than starting
another. `DriveInfo.TotalSize` on a share whose NAS has died blocks for the SMB timeout, the
probe's three-second cap only abandons it, and a "connect all" raises a refresh per drive — so
thirteen dead shares used to pile blocked threads onto the pool until the `HostSpelling`
lookups queued behind them started timing out as well.

## Persistence

`AppDbContext` (`src/Helix.Infrastructure/Database/AppDbContext.cs`) implements both `IDbContext` and `IUnitOfWork` (abstractions in `Helix.Application/Abstractions/Data/`). The SQLite database is encrypted: the connection string is built with a password from `PasswordGenerator.GetOrCreatePassword()`, and `IRelationalCommandBuilderFactory` is replaced with a custom builder (`Database/Sqlite/CustomRelationalCommandBuilderFactory`) to support the cipher. `InsertAuditLogsInterceptor` is registered as a singleton and attached to the context to write audit logs automatically on save. Entity configurations are picked up via `ApplyConfigurationsFromAssembly` from `Database/Configurations/`.

The cipher is **SQLite3 Multiple Ciphers** (`SQLite3MC.PCLRaw.bundle`), not SQLCipher itself.
`SQLitePCLRaw.bundle_e_sqlcipher` is deprecated — SQLitePCLRaw 3.0 stopped shipping free
encrypted builds — and SQLite3MC is the open-source library that reads the SQLCipher format.
Its default cipher is ChaCha20, though, so `DatabaseLocation.DataSource` is a `file:` URI
carrying `cipher=sqlcipher&legacy=4` (`DatabaseConfiguration.CipherParameters`): every
install's `helix.db` was written by SQLCipher 4, and without those parameters it reads as
"file is not a database". The URI is built by hand, escaping only `%`, `?` and `#`, never
through `new Uri(path).AbsoluteUri`: that leaves a `%41` already in the path unescaped, SQLite
decodes it, and a profile folder with one in it opened an empty database somewhere else.
New databases are written in the same format, so a build that an
update rolls back to can still open them. **Do not drop the parameters or change the cipher**
without a migration that rekeys existing files. `SqlCipherCompatibilityTests` opens
`Database/Fixtures/sqlcipher-v4.db`, written by the old bundle, to hold that.

The `DbContext` lifetime and threading were a recurring issue historically; the fix is the per-operation scope pattern above (`ScopedHandler`). Do not resolve `AppDbContext` (or anything scoped) from the root provider, and do not share a context instance across concurrent operations.

## Where the data lives

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



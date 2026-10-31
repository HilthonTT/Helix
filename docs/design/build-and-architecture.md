# Build, tests and architecture

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

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
dotnet ef migrations add <Name> --project src/Helix.Infrastructure --startup-project src/Helix.App --framework net10.0-windows10.0.19041.0
```

`scripts/` wraps these in PowerShell (5.1 or 7), each with `Get-Help` documentation. They
pick the right projects for the host OS, so `test.ps1` on a Mac runs only the Application
suite:

```powershell
./scripts/build.ps1 [-Configuration Release] [-Clean]
./scripts/test.ps1 [-Suite Application,Architecture] [-Filter CreateDriveTests] [-NoBuild] [-Trx]
./scripts/clean.ps1                      # every bin/obj under src and tests
./scripts/publish.ps1 [-Runtime win-x64,win-arm64] [-Zip] [-Tag v2.2.6]   # as release.yml does
./scripts/migration.ps1 add <Name> | remove | list
```

`dotnet ef` also needs `--framework` now that the projects are multi-targeted; `migration.ps1`
passes the host's head.

Running the MAUI app itself is normally done via Visual Studio 2022 (`Helix.App` startup project), not `dotnet run`, because of the MAUI/Windows packaging configuration.

`Application.UnitTests` targets plain `net10.0` and runs anywhere; `Infrastructure.UnitTests`
and `ArchitectureTests` target `net10.0-windows10.0.19041.0`, because the assemblies they load
are the Windows head. On a Mac, build the app project on its own and run the Application suite
— the solution also contains those Windows-only projects:

```bash
dotnet build src/Helix.App/Helix.App.csproj
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj
```

The test projects use **`xunit.v3.mtp-off`**, xunit v3 without Microsoft Testing Platform,
so `dotnet test` keeps running them through VSTest and `--filter "FullyQualifiedName~…"` and
`--logger trx` (which `test.ps1` and CI pass) keep working. Plain `xunit.v3` turns MTP on,
and the .NET 10 SDK then refuses the VSTest path outright. `xUnit1051` (pass
`TestContext.Current.CancellationToken`) is suppressed: the handlers under test run against
substitutes and have nothing to cancel.

xunit v3 runs each test project as its own **executable**, and an executable that references
the Windows App SDK (through MAUI) gets the SDK's bootstrapper, which goes looking for an
installed Windows App Runtime at startup. CI runners have none, so the process hung before
xunit could talk to it ("Test process did not respond within 60 seconds") while every
developer machine passed. `Infrastructure.UnitTests` and `ArchitectureTests` therefore set
`WindowsAppSdkBootstrapInitialize` to false; under xunit 2 they were loaded into the test
host as libraries and the bootstrapper never ran.

CI covers both: a Windows job builds and runs all three suites, a macOS job compile-verifies the
Catalyst head and runs the Application suite.

The macOS jobs in `ci.yml` and `release.yml` install the MAUI workload **pinned to 10.0.401**.
Workload set 10.0.401.1 brought the Mac Catalyst 27.0 SDK, which requires Xcode 27, and the
`macos-latest` image tops out at Xcode 26.6 — an unpinned install failed the build on the SDK's
Xcode check. Lift the pin once the runner image carries the Xcode the latest set asks for.

## Documentation website

`website/` is the documentation site, a Next.js static export with MDX pages under
`app/docs/<page>/page.mdx` and the sidebar order in `lib/docs.ts`. `.github/workflows/website.yml`
builds it on every change under `website/` and deploys `main` to GitHub Pages with
`NEXT_PUBLIC_BASE_PATH=/Helix`; the repository's Pages source has to be **GitHub Actions** for
the deploy job to succeed.

```bash
cd website && npm ci && npm run dev
```

The GIFs and screenshots in `website/public/media` are recorded from the real app by
`website/scripts/record-media.py`. It publishes Helix with `-p:Company=HelixMedia
-p:AssemblyTitle=HelixMedia`, which is what moves `FileSystem.AppDataDirectory` (see **Where
the data lives**) to `%LOCALAPPDATA%\HelixMedia`, so recording never touches a real install's
account; the demo drives use RFC 5737 addresses so no probe reaches a real LAN. The README embeds
two of those GIFs rather than keeping screenshots of its own — re-record rather than adding
images elsewhere.

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

## SharedKernel

Framework-free primitives, in three folders/namespaces:

- `SharedKernel.Abstractions` — `IAuditable`, `IDateTimeProvider`.
- `SharedKernel.Primitives` — `Entity`, `Enumeration`, `Ensure`.
- `SharedKernel.Results` — `Result`, `Result<T>`, `Error`, `ErrorType`, `ValidationError`.

All three are imported through a `GlobalUsings.cs` in every consuming project, so individual files do not write `using SharedKernel...;`. The project deliberately has **no** MAUI or EF Core reference — do not add one.

## Helix.Domain

Entities, domain errors and repository **interfaces**, one folder per aggregate (`Auditlogs/`, `DriveGroups/`, `Drives/`, `Schedules/`, `Settings/`, `Users/`). Also framework-free: no MAUI, no EF Core, no Application/Infrastructure dependency. Error classes are plural (`DriveErrors`, `UserErrors`, `SettingsErrors`, `AuthenticationErrors`).

## Helix.Application

```
Abstractions/   Authentication, Connector, Cryptography, Data, Desktop, Diagnostics,
                Handlers, Security, Startup, Storage, Time, Updates — interfaces only
Core/           Drives, Errors, Security, Sorting, Validation — cross-feature helpers
Features/       one folder per feature, split into Commands / Queries
                Auditlogs/{Commands,Queries}
                Diagnostics/Commands
                DriveGroups/{Commands,Queries}
                Drives/{Commands,Queries,Contracts}
                Schedules/{Commands,Queries,Contracts}
                Settings/{Commands,Queries}
                Storage/{Queries,Contracts}
                Updates/{Commands,Queries}
                Users/Commands
DependencyInjection.cs
```

No dependency on Infrastructure — it consumes its own `Abstractions/` interfaces only.

**No MAUI, either**, and `ApplicationLayer_Should_NotHaveDependencyOn_Maui` in the architecture
tests holds it to that. Three handlers used to raise a file dialog through the static
`FolderPicker.Default` / `FilePicker.Default`, which cost more than it looked: `ImportDrives`,
`ExportDrives` and `ExportDiagnostics` were the only handlers that could not be unit-tested —
and `ImportDrives` is the one that decrypts a vault, validates every DTO and decides which
letters are free — and the whole reference dragged `Application.UnitTests` onto a Windows target
framework, so every Application test was Windows-only for the sake of three dialogs.
`IFolderPicker` and `IFilePicker` sit in `Abstractions/Desktop/` beside `IFileBrowser` and are
implemented in the **presentation** layer (`Services/FolderPickerService`,
`Services/FilePickerService`), the same place `IPassphrasePrompt` is: a picker is a modal the
user answers, not a platform capability, and neither needs an `#if` — the toolkit's
`FolderPicker` and MAUI's `FilePicker` cover both heads.

`IFilePicker` and `IFolderPicker` collide by name with `Microsoft.Maui.Storage.IFilePicker` and
`CommunityToolkit.Maui.Storage.IFolderPicker`, both of which the App project has through
implicit usings — which is why the two implementations qualify one side or the other rather
than importing both namespaces.

`FolderPick` keeps the two failure cases the toolkit reports separately: a dialog the user
dismissed and a dialog that answered with no path are different errors
(`FolderPickerErrors.Cancelled` and `InvalidFolderPath`), and collapsing them into a nullable
string would have lost one.

## Handler pattern (use cases)

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


## Localization

UI strings live in `src/Helix.App/Resources/Languages/AppResources.resx` with translations for `de`, `fr`, `id`, `ja`, `nl`. Use `TranslateExtension` (the `{l10n:Translate}` XAML markup extension) and `LocalizationResourceManager` rather than hard-coding strings.

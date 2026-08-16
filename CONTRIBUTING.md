# Contributing to Helix

Thanks for your interest in improving Helix! This document covers everything you need to get a change merged.

By participating you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Getting started

**Prerequisites**

- [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) with the **.NET MAUI** workload (or the .NET 10 SDK plus `dotnet workload install maui`)
- Windows 10 build 19041 or later — Helix is Windows-only (WinRT/Win32 interop)

```bash
git clone https://github.com/HilthonTT/Helix.git
cd Helix
dotnet build Helix.sln
```

Open `Helix.sln`, set `Helix.App` as the startup project, and run. The app is normally launched from Visual Studio rather than `dotnet run` because of its Windows packaging configuration.

## Before you open a pull request

Run the full check locally — this is exactly what CI runs:

```bash
dotnet build Helix.sln
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj
dotnet test tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
dotnet test tests/ArchitectureTests/ArchitectureTests.csproj
```

A single test:

```bash
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~CreateDriveTests"
```

## Architecture rules

Helix follows Clean Architecture, and the layering is **enforced by tests** (`tests/ArchitectureTests/Layers/LayerTests.cs`, NetArchTest). Dependencies only point inward:

```
SharedKernel → Helix.Domain → Helix.Application → Helix.Infrastructure → Helix.App
```

- `Helix.Domain` — entities and repository *interfaces*. No Application or Infrastructure dependencies.
- `Helix.Application` — feature-sliced use cases plus abstractions. Consumes interfaces only; never references Infrastructure.
- `Helix.Infrastructure` — concrete implementations (EF Core/SQLite, cryptography, NAS connector).
- `Helix.App` — MAUI presentation. References Application and Infrastructure for DI composition only.

If an architecture test fails, the fix is the design, not the test.

### Where things live

**Namespaces mirror folders exactly.** When you move a file, move its namespace with it — the
architecture tests check this for Infrastructure and for Application handlers.

```
src/SharedKernel/           Abstractions/  Primitives/  Results/
src/Helix.Domain/           one folder per aggregate: Auditlogs/ Drives/ Settings/ Users/
src/Helix.Application/      Abstractions/  Core/  Features/<Feature>/{Commands,Queries}
src/Helix.Infrastructure/   Authentication/ Connector/ Cryptography/ Desktop/ Platform/
                            Startup/ Time/
                            Database/{Configurations,Constants,Interceptors,Repositories,Sqlite}
src/Helix.App/              Behaviors/ Common/ Controls/ Converters/ Extensions/ Icons/
                            Localization/ Messaging/ Models/ Services/
                            Views/<Feature>/  ViewModels/<Feature>/
tests/                      mirror the layout of the project under test
```

A view and its viewmodel live in the same feature folder under their respective roots:
`Views/Drives/HomePage.xaml` pairs with `ViewModels/Drives/HomeViewModel.cs`. Messages published
through `WeakReferenceMessenger` go in `Messaging/<Feature>/`, never next to the view that raises
them.

In XAML, use the shared prefixes: `vm:` for the file's viewmodel, `views:` for sibling views,
`l10n:` for `{l10n:Translate Key}`, and `controls:` / `behaviors:` / `converters:` / `icons:` /
`models:` for the matching folders.

NuGet versions are centrally managed in `Directory.Packages.props`, and properties shared by every
project live in `Directory.Build.props` — reference packages without a `Version` attribute, and
don't re-declare `Nullable` or `ImplicitUsings` in a `.csproj`.

### Platform-specific code

Helix ships a Windows head (`net10.0-windows10.0.19041.0`) and a macOS one
(`net10.0-maccatalyst`). Both are gated on the host OS in `Helix.App.csproj` and
`Helix.Infrastructure.csproj`, so `dotnet build` does the right thing on either machine —
but Mac Catalyst needs Xcode, so **the macOS head cannot be compiled from Windows**. CI
builds it on a macOS runner; that is the check that catches a broken Catalyst build.

Keep the `#if` count low. Only three abstractions are genuinely per-OS —
`INasConnector`, `IStartupService`, `IDesktopService` — and they are bound once in
`AddPlatformServices()`. If you need platform behaviour somewhere new, prefer adding it
to one of those (or a new abstraction beside them) over sprinkling `#if WINDOWS` through
a viewmodel. Windows implementations use `[SupportedOSPlatform("windows")]` and stay
compilable everywhere; macOS ones need `#if MACCATALYST` because they reference Apple
BCL types.

The test projects target `net10.0-windows`, so `dotnet test` is Windows-only. On a Mac,
build `src/Helix.App/Helix.App.csproj` directly rather than the solution.

### Adding a use case

Every use case is a `sealed class` implementing `IHandler`:

```csharp
public sealed class CreateDrive(IDriveRepository repo, IUnitOfWork uow, ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(/* inputs */);

    public async Task<Result<Drive>> Handle(Request request, CancellationToken ct = default)
    {
        // validate → authorize → domain rules → mutate → SaveChangesAsync → invalidate cache → return Result
    }
}
```

- Expected failures flow through `Result` / `Result<T>` — **handlers never throw** for them. Errors come from the static error classes (`DriveErrors`, `AuthenticationErrors`, `ValidationErrors`).
- Put it in `src/Helix.Application/Features/<Feature>/Commands` or `.../Queries`, and register it in `src/Helix.Application/DependencyInjection.cs`.
- Handlers are **scoped**. Never cache one in a view model or page field — invoke it through `ScopedHandler.HandleAsync((MyHandler h) => h.Handle(request))` so each operation gets a fresh `AppDbContext`. Only the documented singletons may be resolved from `App.ServiceProvider` and stored in fields.

### Database migrations

Migrations live in `Helix.Infrastructure` but run with the app as startup project:

```bash
dotnet ef migrations add <Name> --project src/Helix.Infrastructure --startup-project src/Helix.App
```

### Localization

User-facing strings belong in `src/Helix.App/Resources/Languages/AppResources.resx` and are consumed via `TranslateExtension` / `LocalizationResourceManager` — never hard-coded in XAML or C#. Translations exist for `de`, `fr`, `id`, `ja` and `nl`; adding the English string is enough, translations can follow.

## Code style

`.editorconfig` at the repository root defines the formatting and C# style rules. Visual Studio applies them automatically; from the CLI:

```bash
dotnet format Helix.sln
```

Conventions used throughout the codebase:

- File-scoped namespaces, 4-space indentation, `sealed` by default for handlers and services.
- Primary constructors for dependency injection.
- Nullable reference types are enabled — keep it warning-free.

## Commits and pull requests

- Keep pull requests focused: one logical change per PR.
- Write a descriptive title; [Conventional Commit](https://www.conventionalcommits.org/) prefixes (`feat:`, `fix:`, `chore:`, `docs:`) are appreciated but not required.
- Fill in the pull request template, and link the issue the PR closes.
- Add or update tests for behaviour changes.
- Include before/after screenshots for UI changes.

## Security

Never commit credentials, connection strings, real NAS addresses, or `.helixvault` files. If you found a vulnerability, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Questions

Open a [discussion](https://github.com/HilthonTT/Helix/discussions) or an issue — happy to help.

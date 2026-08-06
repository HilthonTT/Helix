# Contributing to Helix

Thanks for your interest in improving Helix! This document covers everything you need to get a change merged.

By participating you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Getting started

**Prerequisites**

- [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) with the **.NET MAUI** workload (or the .NET 9 SDK plus `dotnet workload install maui`)
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
- Register the new handler in `src/Helix.Application/DependencyInjection.cs`.
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

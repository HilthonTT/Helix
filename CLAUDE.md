# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Helix is a .NET MAUI Windows desktop app for managing connections to NAS drives. It targets `net9.0-windows10.0.19041.0` and is Windows-only (uses WinRT/Win32 interop in `Helix.App/MauiProgram.cs` and a `IWshRuntimeLibrary` COM reference in `Helix.Infrastructure`). Open `Helix.sln` in Visual Studio 2022 to build/run.

## Commands

Build and test from the repo root:

```bash
dotnet build Helix.sln
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

## Architecture

Clean Architecture with four layers plus a SharedKernel. Dependencies only point inward, and this is enforced by `tests/ArchitectureTests/Layers/LayerTests.cs` using NetArchTest — do not break those rules:

- `SharedKernel` — primitives reused across layers: `Result` / `Result<T>`, `Entity`, `Error`, `Ensure`, `IDateTimeProvider`, `IAuditable`.
- `Helix.Domain` — entities (`Drive`, `User`, `Settings`, `Auditlog`) and repository **interfaces**. No deps on Application or Infrastructure.
- `Helix.Application` — feature-sliced use cases (`Drives/`, `Users/`, `Auditlogs/`, `Settings/`) plus cross-cutting abstractions in `Abstractions/` (`Authentication`, `Caching`, `Connector`, `Cryptography`, `Data`, `Desktop`, `Handlers`, `Startup`, `Time`). No deps on Infrastructure — it consumes interfaces only.
- `Helix.Infrastructure` — concrete implementations: EF Core / SQLite (`Database/`, encrypted via `SQLitePCLRaw.bundle_e_sqlcipher`), `Connector/` (NAS connection via `IWshRuntimeLibrary`), `Cryptography/`, `Caching/`, `Authentication/`, `Desktop/`, `Startup/`, plus `Migrations/`.
- `Helix.App` — MAUI presentation layer (Pages, Modals, Views, ViewModels, Resources/Languages, Behaviors, Converters). References both Application and Infrastructure for DI composition only.

Each layer exposes an `XxxAssembly.cs` marker type (`DomainAssembly`, `ApplicationAssembly`, `InfrastructureAssembly`, `PresentationAssembly`) used by the architecture tests and for assembly scanning.

### Handler pattern (use cases)

Every use case in `Helix.Application` is a `sealed class` implementing the marker interface `IHandler` (`Abstractions/Handlers/IHandler.cs`). The shape is consistent — match it when adding new use cases:

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
        // 6. Invalidate relevant ICacheService keys
        // 7. return Result/Result<T>
    }
}
```

Outcomes flow through `SharedKernel.Result` / `Result<T>` — handlers never throw for expected failures. Errors come from static error classes (e.g. `DriveErrors`, `AuthenticationErrors`, `ValidationErrors`).

### Dependency injection

DI is composed via three static extension methods chained in `Helix.App/MauiProgram.cs`:

- `services.AddApplication()` — `Helix.Application/DependencyInjection.cs` registers every handler as `Scoped`. New handlers must be added here.
- `services.AddInfrastructure()` — `Helix.Infrastructure/DependencyInjection.cs` registers `AppDbContext`, repositories, caching, auth, time, NAS connector, etc.
- `services.AddPresensation()` — Presentation registrations (note: the method name is misspelled but kept consistent across the codebase).

### Persistence

`AppDbContext` (`src/Helix.Infrastructure/Database/AppDbContext.cs`) implements both `IDbContext` and `IUnitOfWork` (abstractions in `Helix.Application/Abstractions/Data/`). The SQLite database is encrypted: the connection string is built with a password from `PasswordGenerator.GetOrCreatePassword()`, and `IRelationalCommandBuilderFactory` is replaced with a custom builder (`CustomRelationalCommandBuilderFactory`) to support the cipher. `InsertAuditLogsInterceptor` is registered as a singleton and attached to the context to write audit logs automatically on save. Entity configurations are picked up via `ApplyConfigurationsFromAssembly` from `Database/Configurations/`.

Recent commits indicate the `DbContext` lifetime and threading have been a recurring issue — be careful changing its lifetime or sharing it across threads.

### Localization

UI strings live in `src/Helix.App/Resources/Languages/AppResources.resx` with translations for `de`, `fr`, `id`, `ja`, `nl`. Use `TranslateExtension` (XAML markup extension) and `LocalizationResourceManager` rather than hard-coding strings.

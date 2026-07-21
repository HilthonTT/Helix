<div align="center">

  <div>
      <img src="https://img.shields.io/badge/.NET-%23512BD4.svg?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET" />
      <img src="https://img.shields.io/badge/C%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white" alt="C#" />
      <img src="https://img.shields.io/badge/Entity%20Framework%20Core-%235C2D91.svg?style=for-the-badge&logo=dotnet&logoColor=white" alt="Entity Framework Core" />
      <img src="https://img.shields.io/badge/.NET%20Maui-%23007FFF.svg?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET MAUI" />
      <img src="https://img.shields.io/badge/xUnit-%2325A162.svg?style=for-the-badge&logo=xunit&logoColor=white" alt="xUnit Tests" />
      <img src="https://img.shields.io/badge/SQLite-%23003B57.svg?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite" />
  </div>

  <h3 align="center">Helix</h3>

  <p align="center">
    A Windows desktop app for managing connections to your NAS drives —<br />
    map network shares to drive letters, monitor their status, and keep your credentials encrypted at rest.
  </p>

</div>

## 📋 <a name="table">Table of Contents</a>

1. 🤖 [Introduction](#introduction)
2. ⚙️ [Tech Stack](#tech-stack)
3. 🔋 [Features](#features)
4. 🔒 [Security](#security)
5. 🏗️ [Architecture](#architecture)
6. 🤸 [Quick Start](#quick-start)
7. 🧪 [Building & Testing](#building-testing)

## <a name="introduction">🤖 Introduction</a>

Helix is a .NET MAUI desktop application for Windows that manages connections to NAS (network-attached storage) drives. It maps network shares to Windows drive letters via the native Win32 WNet API, shows live connection status and storage usage, and stores your drive configurations in an encrypted local database.

## <a name="tech-stack">⚙️ Tech Stack</a>

- .NET 9 / C#
- .NET MAUI (WinUI 3)
- Entity Framework Core
- SQLite (encrypted with SQLCipher)
- xUnit + NetArchTest

## <a name="features">🔋 Features</a>

👉 **Connect/Disconnect NAS Drives:** Map network shares to drive letters individually or all at once. Failed connections — for example an incorrect username or password — are reported with a clear per-drive error message instead of interrupting the app.

👉 **Connection Dashboard:** See at a glance which drives are connected, total storage usage, and a live status chart.

👉 **Auto-Connect on Startup:** Optionally reconnect all your drives when the app launches, and start Helix with Windows.

👉 **Encrypted Import/Export:** Back up your drive configurations to a passphrase-protected `.helixvault` file (AES-256-GCM) and restore them on any machine.

👉 **Audit Logs:** Every drive change is recorded automatically so you can track what happened and when.

👉 **Multi-User:** Each user account sees only their own drives, settings, and audit logs.

👉 **Multilingual:** Available in English, French, German, Dutch, Indonesian, and Japanese.

## <a name="security">🔒 Security</a>

- **Encrypted database:** The local SQLite database is encrypted with SQLCipher. The key is generated with a cryptographically secure RNG and stored in Windows secure storage.
- **Password hashing:** User passwords are hashed with PBKDF2-SHA512 (600k iterations, per OWASP guidance). Older hashes are transparently upgraded on login.
- **Encrypted exports:** `.helixvault` files are encrypted with AES-256-GCM using a key derived from your passphrase (PBKDF2-SHA512).
- **Native credential handling:** Drive credentials are passed to Windows through the `mpr.dll` WNet API — never through a command line where they could be observed.
- **Per-user isolation:** All queries are scoped to the logged-in user; one account can never read another account's drives or logs.

## <a name="architecture">🏗️ Architecture</a>

Helix follows Clean Architecture — dependencies only point inward, enforced by automated architecture tests:

```
src/
├── SharedKernel          # Result, Error, Entity, shared primitives
├── Helix.Domain          # Entities (Drive, User, Settings, Auditlog) + repository interfaces
├── Helix.Application     # Feature-sliced use cases (Drives, Users, Settings, Auditlogs)
├── Helix.Infrastructure  # EF Core/SQLite, NAS connector, cryptography, auth
└── Helix.App             # MAUI presentation layer (pages, modals, view models)

tests/
├── Application.UnitTests
├── Infrastructure.UnitTests
└── ArchitectureTests     # NetArchTest rules that enforce the layering
```

Use cases follow a consistent handler pattern: validate → authorize → apply domain rules → persist → return a `Result`. Expected failures flow through `Result`/`Error` values — handlers never throw.

## <a name="quick-start">🤸 Quick Start</a>

Follow these steps to set up the project locally on your machine.

**Prerequisites**

Make sure you have the following installed on your machine:

- [Git](https://git-scm.com/)
- [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) with the .NET MAUI workload
- Windows 10 (build 19041) or later

**Cloning the Repository**

```bash
git clone https://github.com/HilthonTT/Helix.git
```

Open `Helix.sln` in Visual Studio 2022, set `Helix.App` as the startup project, and run.

## <a name="building-testing">🧪 Building & Testing</a>

Build the whole solution and run the test suites from the repository root:

```bash
dotnet build Helix.sln
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj
dotnet test tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
dotnet test tests/ArchitectureTests/ArchitectureTests.csproj
```

The MAUI app itself (`Helix.App`) is normally launched from Visual Studio 2022 because of its Windows packaging configuration.

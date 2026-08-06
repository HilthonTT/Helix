<div align="center">

  <img src="src/Helix.App/Resources/Images/logo.png" alt="Helix logo" width="96" height="96" />

  <h1 align="center">Helix</h1>

  <p align="center">
    A Windows desktop app for managing connections to your NAS drives —<br />
    map network shares to drive letters, monitor their status, and keep your credentials encrypted at rest.
  </p>

  <p align="center">
    <a href="https://github.com/HilthonTT/Helix/actions/workflows/ci.yml">
      <img src="https://github.com/HilthonTT/Helix/actions/workflows/ci.yml/badge.svg" alt="CI status" />
    </a>
    <a href="https://github.com/HilthonTT/Helix/actions/workflows/codeql.yml">
      <img src="https://github.com/HilthonTT/Helix/actions/workflows/codeql.yml/badge.svg" alt="CodeQL status" />
    </a>
    <a href="LICENSE">
      <img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License" />
    </a>
    <a href="https://github.com/HilthonTT/Helix/releases">
      <img src="https://img.shields.io/github/v/release/HilthonTT/Helix?include_prereleases&label=release" alt="Latest release" />
    </a>
    <img src="https://img.shields.io/badge/platform-Windows%2010%2B-0078D6?logo=windows&logoColor=white" alt="Windows 10+" />
  </p>

  <p align="center">
    <img src="https://img.shields.io/badge/.NET%209-%23512BD4.svg?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 9" />
    <img src="https://img.shields.io/badge/C%23-%23239120.svg?style=for-the-badge&logo=csharp&logoColor=white" alt="C#" />
    <img src="https://img.shields.io/badge/.NET%20MAUI-%23007FFF.svg?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET MAUI" />
    <img src="https://img.shields.io/badge/EF%20Core-%235C2D91.svg?style=for-the-badge&logo=dotnet&logoColor=white" alt="Entity Framework Core" />
    <img src="https://img.shields.io/badge/SQLite%20%2B%20SQLCipher-%23003B57.svg?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite + SQLCipher" />
    <img src="https://img.shields.io/badge/xUnit-%2325A162.svg?style=for-the-badge&logo=xunit&logoColor=white" alt="xUnit" />
  </p>

</div>

## 📋 Table of Contents

1. 🤖 [Introduction](#-introduction)
2. 🔋 [Features](#-features)
3. 🔒 [Security](#-security)
4. 🏗️ [Architecture](#%EF%B8%8F-architecture)
5. 🤸 [Quick Start](#-quick-start)
6. 🧪 [Building & Testing](#-building--testing)
7. 📦 [Publishing to a Folder](#-publishing-to-a-folder)
8. ⚙️ [Continuous Integration](#%EF%B8%8F-continuous-integration)
9. 🤝 [Contributing](#-contributing)
10. 📄 [License](#-license)

## 🤖 Introduction

Helix is a .NET MAUI desktop application for Windows that manages connections to NAS (network-attached storage) drives. It maps network shares to Windows drive letters via the native Win32 WNet API, shows live connection status and storage usage, and stores your drive configurations in an encrypted local database.

## 🔋 Features

👉 **Connect/Disconnect NAS Drives:** Map network shares to drive letters individually or all at once. Failed connections — for example an incorrect username or password — are reported with a clear per-drive error message instead of interrupting the app.

👉 **Connection Dashboard:** See at a glance which drives are connected, total storage usage, and a live status chart.

👉 **Auto-Connect on Startup:** Optionally reconnect all your drives when the app launches, and start Helix with Windows.

👉 **Encrypted Import/Export:** Back up your drive configurations to a passphrase-protected `.helixvault` file (AES-256-GCM) and restore them on any machine.

👉 **Audit Logs:** Every drive change is recorded automatically so you can track what happened and when.

👉 **Multi-User:** Each user account sees only their own drives, settings, and audit logs.

👉 **Multilingual:** Available in English, French, German, Dutch, Indonesian, and Japanese.

## 🔒 Security

- **Encrypted database:** The local SQLite database is encrypted with SQLCipher. The key is generated with a cryptographically secure RNG and stored in Windows secure storage.
- **Password hashing:** User passwords are hashed with PBKDF2-SHA512 (600k iterations, per OWASP guidance). Older hashes are transparently upgraded on login.
- **Encrypted exports:** `.helixvault` files are encrypted with AES-256-GCM using a key derived from your passphrase (PBKDF2-SHA512).
- **Native credential handling:** Drive credentials are passed to Windows through the `mpr.dll` WNet API — never through a command line where they could be observed.
- **Per-user isolation:** All queries are scoped to the logged-in user; one account can never read another account's drives or logs.

## 🏗️ Architecture

Helix follows Clean Architecture — dependencies only point inward, enforced by automated architecture tests (NetArchTest):

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

## 🤸 Quick Start

Follow these steps to set up the project locally on your machine.

**Prerequisites**

Make sure you have the following installed on your machine:

- [Git](https://git-scm.com/)
- [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) with the **.NET MAUI** workload
- Windows 10 (build 19041) or later

**Cloning the Repository**

```bash
git clone https://github.com/HilthonTT/Helix.git
```

Open `Helix.sln` in Visual Studio 2022, set `Helix.App` as the startup project, and run.

## 🧪 Building & Testing

Build the whole solution and run the test suites from the repository root:

```bash
dotnet build Helix.sln
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj
dotnet test tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
dotnet test tests/ArchitectureTests/ArchitectureTests.csproj
```

The MAUI app itself (`Helix.App`) is normally launched from Visual Studio 2022 because of its Windows packaging configuration.

## 📦 Publishing to a Folder

To compile the app into a single deployable folder (e.g. `C:\Helix`) containing `Helix.App.exe` and everything it needs to run, execute this from the repository root:

```bash
dotnet publish src/Helix.App/Helix.App.csproj -f net9.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 -o C:\Helix
```

Then start the app with `C:\Helix\Helix.App.exe`.

**Notes**

- The output is fully **self-contained**: the .NET runtime, the Windows App SDK runtime, and the native SQLCipher library are all included, so the target machine needs nothing pre-installed.
- For ARM64 Windows machines, use `-p:RuntimeIdentifierOverride=win-arm64` instead.
- `-p:RuntimeIdentifierOverride` (rather than the usual `-r`) is required: passing the runtime identifier as a global CLI property would leak into the referenced class libraries and fail the Windows App SDK build. The override is picked up by `Helix.App.csproj` only, which turns it into a self-contained, RID-specific publish.
- The folder is portable — you can copy it to another location or machine and run it from there.
- **User data is not stored in this folder.** The encrypted database lives in the per-user app data directory (`%LOCALAPPDATA%`), so replacing or upgrading the `C:\Helix` folder never touches your drives, settings, or audit logs.

## ⚙️ Continuous Integration

Every push and pull request to `main` runs the [CI workflow](.github/workflows/ci.yml) on a Windows runner:

1. Install the .NET 9 SDK and the MAUI workload
2. Restore and build `Helix.sln` in Release
3. Run the Application, Infrastructure, and Architecture test suites

Alongside it:

- **[CodeQL](.github/workflows/codeql.yml)** scans the C# sources and the workflows themselves on every push, every pull request, and weekly.
- **[Dependabot](.github/dependabot.yml)** opens grouped weekly pull requests for NuGet packages and GitHub Actions.
- **[Release](.github/workflows/release.yml)** builds self-contained `win-x64` and `win-arm64` folders when a `v*` tag is pushed, and attaches them to a GitHub release.

## 🤝 Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for the development setup, the handler pattern, the architecture rules enforced by tests, and the pull request checklist. Participation is governed by our [Code of Conduct](CODE_OF_CONDUCT.md).

Found a security issue? Please report it privately as described in [SECURITY.md](SECURITY.md) rather than opening a public issue.

## 📄 License

Helix is licensed under the [MIT License](LICENSE).

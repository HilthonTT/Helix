<div align="center">

<img src="docs/logo.png" alt="Helix" width="72" />

# Helix

**A desktop app for managing connections to your NAS drives, on Windows and macOS.**

Map network shares to drive letters, monitor their status at a glance,
and keep your credentials encrypted at rest.

<br />

[![CI](https://github.com/HilthonTT/Helix/actions/workflows/ci.yml/badge.svg)](https://github.com/HilthonTT/Helix/actions/workflows/ci.yml)
[![CodeQL](https://github.com/HilthonTT/Helix/actions/workflows/codeql.yml/badge.svg)](https://github.com/HilthonTT/Helix/actions/workflows/codeql.yml)
[![Release](https://img.shields.io/github/v/release/HilthonTT/Helix?include_prereleases&label=release&style=flat-square)](https://github.com/HilthonTT/Helix/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)
![Windows 10+](https://img.shields.io/badge/Windows%2010%2B-0078D6?style=flat-square&logo=windows&logoColor=white)
![macOS 15+](https://img.shields.io/badge/macOS%2015%2B-000000?style=flat-square&logo=apple&logoColor=white)

![.NET 10](https://img.shields.io/badge/.NET%2010-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=flat-square&logo=csharp&logoColor=white)
![.NET MAUI](https://img.shields.io/badge/.NET%20MAUI-007FFF?style=flat-square&logo=dotnet&logoColor=white)
![EF Core](https://img.shields.io/badge/EF%20Core-5C2D91?style=flat-square&logo=dotnet&logoColor=white)
![SQLite + SQLCipher](https://img.shields.io/badge/SQLite%20%2B%20SQLCipher-003B57?style=flat-square&logo=sqlite&logoColor=white)

</div>

<br />

<div align="center">
  <img src="docs/screenshots/dashboard.png" alt="The Helix dashboard, showing storage usage, connection counts and the drive list" width="900" />
</div>

<br />

## Contents

- [Overview](#overview)
- [Screenshots](#screenshots)
- [Features](#features)
- [Platform support](#platform-support)
- [Security](#security)
- [Download and install](#download-and-install)
- [Building from source](#building-from-source)
- [Publishing](#publishing)
- [Project layout](#project-layout)
- [Continuous integration](#continuous-integration)
- [Contributing](#contributing)
- [License](#license)

## Overview

Helix is a .NET MAUI desktop application that manages connections to NAS
(network-attached storage) drives. It mounts network shares through each platform's native
API — the Win32 WNet API on Windows, NetFS on macOS — reports live connection status and
storage usage, and stores your drive configurations in an encrypted local database.

One codebase ships two heads: `net10.0-windows10.0.19041.0` and `net10.0-maccatalyst`. The
domain model, the pages and the viewmodels are shared verbatim; only the handful of services
that have to talk to the operating system differ.

## Screenshots

| Sign in | Audit log |
| --- | --- |
| <img src="docs/screenshots/sign-in.png" alt="Helix sign-in screen" /> | <img src="docs/screenshots/audit-logs.png" alt="Audit log page listing recorded actions" /> |

| Settings | Add a drive |
| --- | --- |
| <img src="docs/screenshots/settings.png" alt="Settings page with account and preference sections" /> | <img src="docs/screenshots/add-drive.png" alt="Add drive dialog" /> |

## Features

**Connect and disconnect drives.** Map network shares individually or all at once. Failed
connections — an incorrect username or password, for example — are reported with a clear
per-drive message instead of interrupting the app.

**Address a NAS however you reach it.** An IPv4 address, an IPv6 address or a hostname —
`192.168.0.10`, `fd00::5`, `nas.local` or plain `MYNAS`. A name keeps working when the
server's DHCP lease moves.

**Test before you save.** Check a host, share and password against the server from the add
and edit dialogs. Nothing is mounted and no drive letter is claimed, so a test is safe to
run against a half-finished form.

**Connection dashboard.** See which drives are connected, total storage usage, and a live
status chart, all on one screen. Each drive row shows when it was last reachable, so an
offline drive tells you whether it dropped minutes or months ago. The storage total counts
each NAS volume once, however many of its shares you have mapped.

**Free drive letters only.** The letter picker offers what is actually available, asking the
operating system as well as Helix — so a letter already taken by a USB stick is never offered
and then rejected at connect time.

**Runs from the notification area.** A tray icon shows how many drives are up, connects or
disconnects any of them from its menu, and raises a notification when one drops or comes
back. Auto-minimize puts the window away there rather than onto the taskbar. Windows only —
on macOS the window stays in the Dock.

**Auto-connect on startup.** Optionally reconnect drives when the app launches, and start
Helix when you sign in. Auto-connect can be turned off per drive, so the ones you only want
on demand stay down until you ask — "connect all" still connects everything.

**Remembered mappings.** Mark a drive to be restored at Windows sign-in, and Explorer brings
it back without Helix running. Windows only; the switch is hidden on macOS rather than shown
and ignored.

**Encrypted import and export.** Back up your drive configurations to a passphrase-protected
`.helixvault` file (AES-256-GCM) and restore them on any machine — including one running the
other operating system.

**Audit log.** Every drive change is recorded automatically, so you can see what happened
and when — including drops and automatic reconnects that happened while the window was
away. Entries are shown in your own language, and can be trimmed to a retention period you
choose in Settings (0 keeps everything).

**Diagnostic logs.** Helix keeps a rolling log of what it did unattended, in your app data
folder for 14 days. Settings has an **Export** button that saves them as a zip to attach to
a bug report.

**Check for updates.** Settings has a **Check for updates** button that compares your build
against the latest release on GitHub and offers to open its page. It only ever reads and
reports — nothing is downloaded or installed, and pre-releases are ignored.

**Multi-user.** Each account sees only its own drives, settings, and audit entries.

**Light and dark themes.** The interface follows the system theme.

**Multilingual.** Available in English, French, German, Dutch, Indonesian, and Japanese.

## Platform support

What is persisted is identical on both platforms; what differs is how a mount is expressed
and which desktop integrations exist.

| | Windows | macOS |
| --- | --- | --- |
| Minimum version | Windows 10 build 19041 | macOS 15 |
| Mount target | a drive letter, `Z:\` | a folder, `~/Helix Drives/Z` |
| Mount API | `mpr.dll` WNet | `NetFSMountURLSync` |
| Start at sign-in | shortcut in the Startup folder | LaunchAgent plist |
| Desktop shortcut | `.lnk` on the Desktop | symlink to the `.app` |
| Tray icon | yes | no |
| Persistent mapping | yes (`CONNECT_UPDATE_PROFILE`) | not available |

macOS has no drive letters, so a drive's "letter" names a directory under the mount root
instead. Everything else — the encrypted database, the audit log, vault import and export —
behaves the same on both.

## Security

- **Encrypted database.** The local SQLite database is encrypted with SQLCipher. The key is
  generated with a cryptographically secure RNG and held in the platform's secure store
  (Windows secure storage, the macOS Keychain). If the key cannot be persisted, Helix refuses
  to create the database rather than leaving you one that could never be reopened.
- **Password hashing.** Account passwords are hashed with PBKDF2-SHA512 (600,000 iterations,
  per OWASP guidance). Older hashes are transparently upgraded on sign-in.
- **Encrypted exports.** `.helixvault` files are encrypted with AES-256-GCM using a key
  derived from your passphrase via PBKDF2-SHA512.
- **Native credential handling.** Drive credentials are passed to the operating system through
  its mount API, never through a command line where another process could observe them.
- **Per-user isolation.** All queries are scoped to the signed-in user; one account can
  never read another account's drives or logs.
- **Read-only update checks.** The update check is a single unauthenticated GET to the GitHub
  releases API. Helix never downloads or runs anything on your behalf.

Found a security issue? Please report it privately as described in [SECURITY.md](SECURITY.md)
rather than opening a public issue.

## Download and install

Builds are attached to each [GitHub release](https://github.com/HilthonTT/Helix/releases).

**Windows.** Download `Helix-<tag>-win-x64.zip` (or `win-arm64` on an ARM machine), unzip it
anywhere, and run `Helix.App.exe`. The build is self-contained — the .NET runtime, the Windows
App SDK runtime and the native SQLCipher library are all inside the folder, so the machine
needs nothing pre-installed.

**macOS.** Download `Helix-<tag>-macos.zip` and drag `Helix.app` to `/Applications`. The
bundle is universal (Intel and Apple Silicon) but **unsigned and un-notarized** — signing it
needs a paid Apple Developer identity, which cannot live in a public repository. Gatekeeper
will refuse the first launch: right-click the app, choose **Open**, and confirm, or run
`xattr -dr com.apple.quarantine /Applications/Helix.app`. Only do that for a build you
downloaded from the releases page here.

Your data is never kept next to the app. The encrypted database and the logs live in the
per-user app data directory, so replacing or upgrading the app never touches your drives,
settings, or audit entries.

## Building from source

You will need:

- [Git](https://git-scm.com/) and the [.NET 10 SDK](https://dotnet.microsoft.com/download)
- On Windows: [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) with the
  **.NET MAUI** workload
- On macOS: Xcode, plus the MAUI workload (`dotnet workload install maui`)

```bash
git clone https://github.com/HilthonTT/Helix.git
cd Helix
dotnet build Helix.slnx
```

`Helix.App` and `Helix.Infrastructure` gate their target frameworks on the host OS, so the
build produces the right head on either machine without a `-f` switch.

On Windows, open `Helix.slnx` in Visual Studio 2022, set `Helix.App` as the startup project
and press F5 — the app is launched from the IDE rather than with `dotnet run` because of its
Windows packaging configuration.

On macOS, build the app project on its own. `Helix.slnx` also contains the test projects,
which target `net10.0-windows` and cannot be built there:

```bash
dotnet build src/Helix.App/Helix.App.csproj
```

### Tests

The suite targets `net10.0-windows10.0.19041.0`, so it runs on Windows.

```bash
dotnet test Helix.slnx

dotnet test tests/Application.UnitTests/Application.UnitTests.csproj
dotnet test tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
dotnet test tests/ArchitectureTests/ArchitectureTests.csproj
```

A single test:

```bash
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~CreateDriveTests"
```

`ArchitectureTests` is not decoration — it enforces the layer dependency rules and the
namespace-mirrors-folder convention, and a change that breaks either fails the build.

### Database migrations

Migrations live in `Helix.Infrastructure`, but the EF Core tooling is referenced from
`Helix.App`, so the App is the startup project:

```bash
dotnet ef migrations add <Name> --project src/Helix.Infrastructure --startup-project src/Helix.App
```

## Publishing

### Windows — a self-contained folder

To compile the app into a single deployable folder (for example `C:\Helix`) containing
`Helix.App.exe` and everything it needs to run:

```powershell
Remove-Item C:\Helix -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish src/Helix.App/Helix.App.csproj -c Release -f net10.0-windows10.0.19041.0 -p:RuntimeIdentifierOverride=win-x64 -o C:\Helix
```

The `dotnet publish` line is a single line on purpose, so it can be pasted into either shell
unchanged. Only the cleanup differs — in Command Prompt use `rd /s /q C:\Helix` instead of
`Remove-Item`. Then start the app with `C:\Helix\Helix.App.exe`.

**Notes**

- **Clear the output folder first.** `dotnet publish -o` only adds and overwrites files; it
  never deletes ones the build no longer produces. A stale `resources.pri` left over from an
  earlier publish shadows the current `Helix.App.pri`, and because `MauiImage` assets are
  rasterised to scale-qualified files (`logoipsum.scale-100.png`, …) and resolved by name
  through that index, every image in the app silently renders blank.
- For ARM64 machines, use `-p:RuntimeIdentifierOverride=win-arm64` instead.
- **`-f` is required.** `Helix.App` targets more than one framework, and `dotnet publish`
  refuses to guess which to produce — omitting it fails with `NETSDK1129: The 'Publish'
  target is not supported without specifying a target framework`. Only the framework for the
  machine you are on is available to select.
- `-p:RuntimeIdentifierOverride` (rather than the usual `-r`) is required. Passing the runtime
  identifier as a global CLI property would leak into the referenced class libraries and fail
  the Windows App SDK build. The override is read by `Helix.App.csproj` only, which turns it
  into a self-contained, RID-specific publish.
- The folder is portable — copy it to another location or machine and run it from there.

### macOS — a universal `.app`

Folder publishing is a Windows concept; the Catalyst head produces an `.app` bundle instead.
On a Mac:

```bash
dotnet publish src/Helix.App/Helix.App.csproj -f net10.0-maccatalyst -c Release -p:CreatePackage=false -p:EnableCodeSigning=false
```

**Notes**

- **Do not pass `-o`.** On Apple targets `-o` sets `PublishDir`, and the bundle's *contents*
  are written into it rather than an `.app` wrapper. Left alone, the bundle lands at its
  default path under `src/Helix.App/bin/Release/net10.0-maccatalyst/`.
- The project already declares `maccatalyst-x64;maccatalyst-arm64`, so both slices are
  `lipo`-ed into one universal bundle. Do not repeat them as `-p:RuntimeIdentifiers="a;b"` —
  MSBuild splits a `-p:` argument on `;` and reads the second half as a stray switch
  (`MSB1006`), quotes or no quotes.
- Archive it with `ditto -c -k --sequesterRsrc --keepParent`, not `zip`: a plain zip flattens
  the symlinks, permissions and extended attributes that make the bundle launchable.
- The Catalyst head deliberately ships with the **App Sandbox disabled**. A sandboxed process
  cannot mount a network filesystem, write a LaunchAgent or touch the real Desktop — that is
  every platform service at once. Helix is therefore Developer ID / direct distribution, not
  Mac App Store.
- Producing a build others can run without the Gatekeeper prompt needs a Developer ID
  certificate and notarization, which this repository does not have.

## Project layout

Clean Architecture — four layers plus a SharedKernel, with dependencies pointing inward only.

```
src/
  SharedKernel/          Result, Error, Entity, Enumeration — framework-free
  Helix.Domain/          entities, domain errors, repository interfaces
  Helix.Application/     use cases (handlers) and the abstractions they depend on
  Helix.Infrastructure/  EF Core, SQLCipher, platform services, the update check
  Helix.App/             the MAUI head: XAML pages, viewmodels, tray, localization
tests/
  Application.UnitTests/
  Infrastructure.UnitTests/
  ArchitectureTests/     enforces the layer and namespace rules
```

Every use case is a sealed handler under `Helix.Application/Features/` that returns
`Result` / `Result<T>` rather than throwing for expected failures, and the presentation layer
invokes it through a fresh DI scope per operation. [CONTRIBUTING.md](CONTRIBUTING.md) covers
the pattern in full; `CLAUDE.md` records the reasoning behind the sharper edges.

## Continuous integration

Every push and pull request to `main` runs the [CI workflow](.github/workflows/ci.yml):

- **Build & Test (Windows)** — installs the .NET 10 SDK and the MAUI workload, builds
  `Helix.slnx` in Release, and runs the Application, Infrastructure and Architecture suites.
- **Build (macOS)** — compile-verifies the Mac Catalyst head, which is the only place
  Infrastructure's macOS platform services get built at all.

Alongside it:

- **[CodeQL](.github/workflows/codeql.yml)** scans the C# sources and the workflows themselves
  on every push, every pull request, and weekly.
- **[Dependabot](.github/dependabot.yml)** opens grouped weekly pull requests for NuGet packages
  and GitHub Actions.
- **[Release](.github/workflows/release.yml)** runs when a `v*` tag is pushed: self-contained
  `win-x64` and `win-arm64` folders from a Windows runner and an unsigned universal `.app`
  from a macOS runner, all attached to a GitHub release.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development setup,
the handler pattern, the architecture rules enforced by tests, and the pull request checklist.
Participation is governed by our [Code of Conduct](CODE_OF_CONDUCT.md).

## License

Helix is licensed under the [MIT License](LICENSE).

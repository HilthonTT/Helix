# Update check and installer

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## The update check

`GitHubUpdateChecker` reads `/releases/latest` — unauthenticated, read-only, nothing
downloaded or installed. `/releases/latest` rather than the tag list on purpose: tags exist
for things never released, and it excludes pre-releases, which an unattended NAS tool
should not be nudging people onto.

Version comparison goes through `ReleaseVersion`, and it must. `ApplicationDisplayVersion`
is three-part (`2.2.1`) to match the release tags, Windows reports the running build padded
to four (`2.2.1.0`), and `Version` treats a missing component as **-1, not 0** — so an
unnormalized compare makes the running build "older" than the release it was built from and
announces an update to itself. Both sides are widened to four components first. A tag that
is not a version (`nightly`) is refused rather than guessed at.

Bumping `ApplicationDisplayVersion` is **not** enough on its own. What Windows reports is
stamped out of the `Package.appxmanifest` MAUI generates under `obj`, and that generated
copy is incremental on the manifest in `Platforms/Windows` — which deliberately never
carries a version, so a version bump does not invalidate it. Left alone, the app goes on
reporting the previous version in the sidebar, on the sign-in pages and to the update
check until somebody deletes `obj`; the giveaway is an `AssemblyInfo.cs` whose
`AssemblyFileVersion` is the new number while its `AppInfo.Version` metadata is the old
one. The `_HelixRefreshStampedAppxManifest` target in `Helix.App.csproj` deletes the stale
copy when its version no longer matches `ApplicationDisplayVersion.ApplicationVersion`, so
the build regenerates it — do not remove it, and expect to bump nothing but the `.csproj`.

`ApplicationDisplayVersion` is not, on Windows, where the running version comes from.
The resizetizer targets XmlPeek `Identity/@Version` out of
`Platforms/Windows/Package.appxmanifest` into
`[AssemblyMetadata("Microsoft.Maui.ApplicationModel.AppInfo.Version")]`, and that is what
`AppInfo.Current.VersionString` returns for an unpackaged build — the sidebar and the
update check both read it. MAUI only stamps `ApplicationDisplayVersion` into that
attribute when it is empty or the literal `0.0.0.0`, so **leave the manifest on the
placeholder**: a real number written there wins over the `.csproj` and the app announces
an update to itself. The stamped value is `ApplicationDisplayVersion` with
`ApplicationVersion` as a fourth component (`2.2.1.3`), which is why what is compared is
four-part and what is shown is not — `ReleaseVersion.ToDisplayString` reduces it for the
update dialog.

`UpdateCheck` carries both versions in that display form — `LatestVersion` is the tag run
through `ToDisplayString`, not the raw tag. It used to be the raw `v2.3.5` beside a reduced
`CurrentVersion`, and the sidebar badge, which prefixes its own `v`, read `vv2.3.5`.

`Common/VersionInfo` reduces the same string back to three components for the UI, so what
is shown is exactly the tag on the releases page. It also strips a `+<commit>` suffix
first: the build emits both a file version and an informational one, and
`Version.TryParse` rejects the latter, which would otherwise put a commit hash in the UI.
Both places that display the version go through it — the sidebar footer via
`AppShell.AppVersion`, the sign-in pages via `BaseViewModel.AppVersion` — so the two can
never disagree about what is running.


## Installing an update

`GitHubUpdateChecker` also reads the release's `assets` and picks the archive whose name
carries a moniker this machine can run — `win-x64`, `win-arm64`, `macos`, matching what the
release workflow publishes. The match is on `-{moniker}.` and has to stay exact enough that
an x64 machine can never be handed the arm64 build, which installs cleanly and then will
not start. A release with no asset for this machine still reports the update; only the
release page is offered.

`UpdateConfiguration.AssetMonikers` asks the **operating system** its architecture, not
this process, and returns candidates best-first. What gets replaced is the whole install
folder, which the helper then starts fresh, so the question is what the machine can run
rather than what happens to be running: a 32-bit build on 64-bit Windows reported x86, had
no asset, and could never update itself out of that state. Arm64 lists `win-x64` behind
`win-arm64` because Windows on Arm runs x64 under emulation, so a release carrying no Arm64
archive is still installable; the fallback only ever runs in that direction, and
`SelectAsset` walks the list in order rather than matching it all at once, which is the
whole of what keeps that safe.

`UpdateInstaller` splits the work at the point it stops being undoable. `StageAsync`
downloads, checks the bytes against the digest GitHub published, unpacks, and checks that
what came down contains `Helix.App.exe` (or a `.app` bundle), all while Helix runs and with
the install untouched — every failure before this point costs nothing, and none of them
leave the download behind. `Apply` writes a helper script, starts it and returns; the caller
**must quit immediately**, because the helper is waiting on this process to exit before it
moves anything. The helper moves the install aside rather than writing over it, so a
failure halfway puts back exactly what was there; the moved-aside copy is deleted only once
the copy has finished.

The helper is started with its **`WorkingDirectory` set to its own folder**, and that line
is load-bearing on Windows. A child process with no working directory set inherits the
parent's, which for Helix is the install folder — Explorer starts an app there, and
`WindowsStartupService` and `WindowsDesktopService` both set it there explicitly in the
shortcuts they write. A process holds a handle to its current directory and Windows will
not rename a held folder, so the move that puts the install aside failed with a sharing
violation every time, the script's `catch` swallowed it, and the last line started the old
build again: 2.2.0 "updated" to 2.2.1, restarted, and was still 2.2.0. PowerShell's
`Set-Location` inside the script does not fix this — it moves the shell's location, not the
process's current directory, and it is the process handle that holds the folder.

`IsSupported` checks the install folder's **parent** as well as the folder: the helper
renames the install and creates `<install>.old` beside it, so Modify rights on
`C:\Program Files\Helix` without them on `Program Files` downloaded every release, failed
every move and restarted the old build.

The move is retried for `MoveAttempts` seconds, because an indexer or a virus scanner
holding the folder for a moment should not cost the release.

The helper replaces the **whole folder** the executable runs from, and the release zip has
no wrapper folder, so "extract here" in Downloads puts `Helix.App.exe` straight into
Downloads. `IsSafeToReplace` refuses to stage or apply from a shell folder, a drive root, a
folder without the executable, or anywhere under the staging root, with
`UpdateErrors.UnsafeInstallLocation`; without it the swap would have moved Downloads aside
and deleted it. The helper also **gives up if Helix is still running** after its wait,
rather than starting a second copy beside the first; restarts Helix with
`-WorkingDirectory $install` so the new process does not inherit the helper's folder; and,
when a restore finds the install folder still standing because a half-copied file is
held, copies the backup's contents back over it rather than `Move-Item`, which would have
put the backup *inside* the broken install. A stale `.old` that cannot be removed is
logged and the update skipped, so the script never dies before the restart line. Only
assets ending in `.zip` are considered, so a future `.sha256` sidecar cannot be staged as
the build.

`MauiProgram` holds a named mutex per logon session and a second instance brings the
first's window forward and exits (see **The command line**, which that second instance now
also carries): two Helixes on one database is what the helper
produced, and what a shortcut double-clicked while the first is in the tray produces. Whatever happens is appended
to `helix-updates.log` in the log directory, named so `LogFileWriter` collects it into the
diagnostics zip: the helper outlives the logger, and its failure path puts the old version
back and starts it, which is indistinguishable from success unless it is written down.

Neither script carries a comment of its own and both are built line by line rather than as
raw string literals. Each lives in a branch excluded on the other platform, and the
preprocessor still scans excluded regions for directives: any line starting with `#` — a
shell comment, a shebang — reads as one and fails the other head's build.

Two things are checked before the archive is opened, and they answer different questions.

The **digest** is the SHA-256 the release API publishes for each asset, compared against
the hash taken as the download is written rather than by reading the file back; a mismatch
is `UpdateErrors.DownloadCorrupt`, distinct from an unreadable archive because a truncated
or substituted file may well open perfectly well. A release that publishes **no** digest, or
one in an algorithm this build does not know, is staged anyway: GitHub only began returning
the field recently, so refusing those would break updating for exactly the installs furthest
behind, and a hard failure on an unknown algorithm would let one change at GitHub's end stop
every install at once.

The **signature** is the half the digest cannot prove. GitHub publishes the asset as well as
its hash, so the digest only says the bytes arrived intact — `ReleaseSignature` checks a
detached ECDSA P-256 signature over that same hash against a public key compiled into the
build, whose private half lives only in the release workflow's `RELEASE_SIGNING_KEY` secret.
ECDSA rather than Ed25519 because the BCL has it, and an update path is the last place to
take a dependency on a third-party crypto library.

The release publishes **one** manifest, `Helix-<tag>-signatures.txt`, whose lines are
`<asset name> <base64 signature>`, rather than a `.sig` beside each archive — and that name
is load-bearing. Every build published up to v2.2.3 picks its download with
`Name.Contains($"-{moniker}.")` and **no extension filter**; the `.zip` filter is newer than
every release. `Helix-<tag>-win-x64.zip.sig` satisfies that predicate, so a sidecar would
have let an existing install download the signature as its update and fail with "does not
look like Helix", with the order GitHub returned the assets in deciding which it got.
`Helix-<tag>-signatures.txt` carries no moniker and none of them can select it —
`TheSignatureManifestName_Should_NotMatchWhatOlderBuildsSelectOn` guards exactly that.
**Do not rename it to anything carrying a moniker.** Which line is read is decided by asset
name in `ReleaseSignature.FindSignature`, so an x64 machine is never checked against the
arm64 build's signature.

`UpdateConfiguration.SigningPublicKey` is empty, and while it is empty nothing is checked
and updates go on being verified by digest alone — a build that demanded a signature no
published release carries could not update itself at all. Once a key is set, a missing or
wrong signature is fatal (`UpdateErrors.SignatureMissing`, `UpdateErrors.SignatureInvalid`);
leniency there would make the check decorative. Which is why the order matters: **publish a
signed release before shipping the build that pins the key**. `docs/release-signing.md` has
the key generation, the rotation story and what this still does not prove — the archives are
not code-signed, so SmartScreen and Gatekeeper know nothing about them, and a first download
from the releases page trusts GitHub and the account exactly as before.

Staged downloads do not accumulate. `StageAsync` prunes every other release folder under
the staging root before it starts — they are installed or abandoned, and each is an
unpacked build of a couple of hundred megabytes — and the swap script deletes the folder it
copied out of, but only after the copy has succeeded, since until then it is the only copy
of the new version. The helper's own folder is never pruned: a helper started seconds ago
may still be running from it. `ReleaseFolderOf` finds what the helper should delete by
climbing to the child of the staging root rather than by taking the path apart, because the
payload sits a folder or two below it (`unpacked`, and on macOS the `.app` inside that);
anything not under the staging root at all yields nothing to delete, and both scripts guard
on that before removing anything.


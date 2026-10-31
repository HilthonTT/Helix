# Infrastructure and platform seams

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Helix.Infrastructure

Concrete implementations, one folder per abstraction group:

```
Authentication/  Connector/  Cryptography/  Desktop/  Diagnostics/  Platform/
Startup/  Storage/  Time/  Updates/
Database/
    AppDbContext.cs, AppDbContextFactory.cs
    Configurations/   EF entity configurations
    Constants/        table names, connection settings
    Interceptors/     InsertAuditLogsInterceptor
    Repositories/     the IXxxRepository implementations
    Sqlite/           SQLCipher command-builder plumbing
Migrations/
DependencyInjection.cs
```

## Platform seams

Exactly seven abstractions have a genuinely per-OS implementation, and they are bound in
`AddPlatformServices()` behind `#if WINDOWS` / `#elif MACCATALYST` (with an `#else` that
throws, so a new head fails at composition rather than at first use):

| Abstraction | Windows | macOS |
|---|---|---|
| `INasConnector` | `WindowsNasConnector` — `mpr.dll` WNet, mounts to `Z:` | `MacNasConnector` — `NetFSMountURLSync`, mounts to `~/Helix Drives/Z` |
| `IStartupService` | `WindowsStartupService` — `.lnk` in the Startup folder | `MacStartupService` — LaunchAgent plist |
| `IDesktopService` | `WindowsDesktopService` — `.lnk` on the Desktop | `MacDesktopService` — symlink to the `.app` |
| `ITrayIcon` | `WindowsTrayIcon` — `Shell_NotifyIcon`, hidden window on its own message loop | `MacTrayIcon` — AppKit `NSStatusItem` through the ObjC runtime, `UNUserNotificationCenter` |
| `IStorageProbe` | `WindowsStorageProbe` — mounts are `Z:\` | `MacStorageProbe` — mounts are `~/Helix Drives/Z` |
| `IIdleTimeProvider` | `WindowsIdleTimeProvider` — `GetLastInputInfo` | `MacIdleTimeProvider` — `CGEventSourceSecondsSinceLastEventType` |
| `INetworkLocation` | `WindowsNetworkLocation` — default gateway's MAC via `SendARP` | `MacNetworkLocation` — `route get default` and `arp` |

`INasConnector.GetMountPath` is the seventh thing that needs to know where a letter
lives, after the two connectors and the two probes. It is asked of the connector because
the connector is what put it there; it answers for any letter, mounted or not.

`IWakeOnLan` is not a seam either. Sending the packet is a UDP broadcast on both heads; only
the ARP lookup is Windows-only, and it sits behind an `#if WINDOWS` inside the one
implementation, the same way `WindowsNetworkLocation.ProfileName` does, with `CanLookUp`
saying which.

Opening that folder goes through `IFileBrowser`, which is deliberately **not** a seventh
platform seam: `ProcessStartInfo.UseShellExecute` hands a directory to Explorer on Windows
and to `open` on macOS, so one implementation covers both and it is registered outside
`AddPlatformServices()`. The drive row's folder button is the whole reason it exists — the
point of the app is that `Z:` is there, and until this there was no way to go and look at
it.

Both storage probes derive from `StorageProbe`, which holds the part that matters: **one
reading per volume, not per drive**. Several mapped drives are usually several shares of
one NAS pool, and each reports that pool's entire size, so adding the letters up read a
43.2 TB QNAP mapped thirteen times as 562 TB.

Two mounts are treated as one volume when they report the **same total size, to the byte**.
That is a property of the filesystem: it is identical across every share of a pool and it
does not move while the probe runs. Three other ideas were tried against that real
thirteen-share NAS and all failed — do not reintroduce them:

- **The volume serial number** (`GetVolumeInformation`). Looks precise, is not, for SMB:
  Samba and most NAS firmware derive it per share, so thirteen shares gave thirteen
  serials and nothing merged. Volume labels are per-share for the same reason.
- **The free byte count**, as part of the key. Free space drifts continuously on a NAS
  anything is writing to — across those thirteen shares it spanned ~12 MB with no two
  readings equal — so requiring it to match merged nothing either.
- **The host**, as part of the key. It splits one NAS added twice under two spellings (by
  IP and by name), and that fails in the damaging direction: over-counting.

The accepted cost is that two volumes of byte-identical size are counted once. Real volume
sizes are not round numbers, so that means two identically built volumes, and it
understates rather than multiplies. The smallest free reading in a group is the one kept,
so the figure does not flicker as parallel probes finish in a different order.

Do not "simplify" the dashboard total back into a sum over drive letters.

Both connectors take the NAS password as a separate credential argument rather than
putting it in a command line — do not "simplify" either into a `net.exe` or
`mount_smbfs //user:pass@host` shell-out.

Windows allows **one credential context per server for the whole logon session**, which is
why `WindowsNasConnector` does three things it would otherwise not need to.

It holds a per-host `SemaphoreSlim` around each mount, so the thirteen shares of one NAS
that "connect all" and the drive groups hand over at once do not race to establish that
context and come back `ERROR_SESSION_CREDENTIAL_CONFLICT` (1219). The gate's wait is
capped at the mount timeout, so an absent NAS cannot turn "connect all" into five seconds
per drive; going ahead ungated is safe there, because a server that is not answering has
no session to conflict over.

When a mount hits 1219 anyway, **what happens next turns entirely on whether any drive
letter is still mounted from that server** (`HasLiveConnectionTo`), and the two branches
must not be collapsed into one:

- **Something is using it.** A share mapped by hand in Explorer, or the first of this
  NAS's own thirteen drives. Windows will not hold a second credential set for that
  server while the first is in use, so the mount is retried **with no credentials at
  all** — how a share is asked onto an existing session — and comes up on whatever
  account that session belongs to. This is the branch that stopped one drive working and
  twelve failing.
- **Nothing is using it.** The context is a leftover: a session outlives the last
  connection to it, invisible to `net use` and unrelated to Credential Manager, and the
  deviceless session `Test` opens is a common way to leave one behind. Joining it would
  mount the share while proving nothing about the credentials, so **a wrong password
  would go on working** until the leftover expired. The session is dropped instead
  (`DropIdleServerSession`) and the drive's own credentials get a real attempt, whose
  error — a plain logon failure, usually — is what gets reported.

`HasLiveConnectionTo` can only answer for sessions that carry a **drive letter**, and a
deviceless one — a backup client that mounts the NAS at boot, an Explorer window left on a
UNC path, a leftover of Helix's own `Test` — is indistinguishable from nothing at all. So
the drop is refused by whatever holds it and the honest retry conflicts a second time.
**That second conflict is the tell**: a real leftover would have gone. The session is
therefore treated as the first branch after all and joined with no credentials, rather than
the drive giving up. Every drive of a NAS failing at boot because a backup had opened it
first is what the missing third case cost. Only a retry that comes back with something
*other* than 1219 — a logon failure, a missing share — is reported as the failure it is.

The wrong-password guarantee is untouched by that, because it only ever applied to the
genuinely idle case: there the drop succeeds and the retry is a real authentication. A
session that refuses to be dropped was never going to test a password.

`Test` takes the same first branch for the same reason, and more urgently: a connection
test answered out of a session nobody is using has tested nothing. It deliberately does
**not** gain the join-it-anyway fallback — a test that mounts on somebody else's session
has tested nothing either, so a conflict it cannot clear is reported as a conflict.

Dropping the session is only ever done on the second branch. On the first,
`WNetCancelConnection2` against a server name would take down the user's Explorer
mappings and every Helix drive already mounted on that NAS — never do that from a
background reconnect. Even on the second branch the cancel is **unforced**, so a
deviceless connection held by some other process refuses it rather than being broken.

When even the join is refused, one thing is left: Windows keys credentials per server
**name string**, not per machine, so `\192.168.1.6` and `\NAS` are two slots for one NAS.
`TryAlternateSpelling` asks `HostSpelling` for the other spelling and mounts under it,
where there is no session to conflict with and the drive's own credentials get a clean
attempt. It is a last resort and must stay one: its cost is a second credential context to
a NAS that already has one, which is the state that produces these conflicts. A failure
there returns null rather than an error, so what gets reported is still the conflict that
actually stopped the drive.

`Drive.ConnectByHostname` is the same trick asked for in advance — mount under the server's
name rather than the address typed into `Host`, so the collision never happens. It is worth
more than the fallback, because it never joins anybody's session: the drive comes up on its
own credentials, so a wrong password is still reported as one. Off by default, a no-op
unless the host is an IP literal, and hidden on macOS (`DrivePlatform.SupportsHostnameConnect`)
where NetFS asks for credentials per mount and the spelling buys nothing.

`HostSpelling` caches every answer for the life of the process, **including the failures**:
a home router with no PTR records for its DHCP leases is the normal case, not the
exception, and a lookup per drive per sweep would put thirteen of them on the critical path
of every reconnect. Lookups are capped at 1.5s and abandoned rather than cancelled, because
the BCL's synchronous resolver takes no token. A lookup that answers nothing leaves the
address as typed — failing a mount because DNS was quiet would turn an optional improvement
into a new way to lose a drive.

That normal case reaches the BCL as an **exception**: `Dns.GetHostEntry` throws
`SocketException` for an address with no PTR record, and `GetHostAddresses` does the same
for a name that does not resolve. Both are caught where they are raised, so "nothing" is a
return value rather than something for the blanket `catch` in `Within` to mop up — which
matters now that the drive diagnosis calls this on the ordinary path rather than only on a
credential conflict. A lookup that outlives its 1.5s cap is abandoned, and its exception is
observed on the way past: an abandoned task that faults with nobody watching is an
`UnobservedTaskException` raised from the finalizer, minutes later, with no way to tell
what asked for it.

The limit worth knowing, because no amount of code moves it: while other shares of a NAS
are mounted, a drive on that NAS mounts on the existing session and its stored password
is never checked. Editing a password and reconnecting that one drive will appear to work
whatever you type. Only with nothing else mounted from that server does the password get
tested.

macOS has no drive letters, so `Drive.Letter` names a directory under the mount root
instead. The persisted domain model is identical on both platforms.

`Drive.Host` accepts an IPv4 address, an IPv6 address or a hostname, so each connector
renders it into its own platform's form: Windows encodes IPv6 as `ipv6-literal.net`
because a UNC path cannot contain a colon, macOS brackets it for the `smb://` URL.
`Drive.Persistent` is Windows-only — it selects `CONNECT_UPDATE_PROFILE` over
`CONNECT_TEMPORARY`, and the Mac connector ignores it because NetFS has no equivalent.

`MacTrayIcon` is the one place Helix talks to AppKit, and it has to do it the long way:
Mac Catalyst binds UIKit, not AppKit, so there is no `NSStatusBar` type to call. AppKit is
nevertheless loaded in every Catalyst process, and `NSStatusBar`, `NSMenu` and `NSMenuItem`
are public classes, so they are driven through `objc_msgSend` with one typed overload per
signature — never the variadic form, which is wrong on arm64. Clicks arrive on an
`NSObject` subclass exported as `helixItemClicked:`, and each leaf's `tag` is its position in
the whole menu tree, the same numbering `WindowsTrayIcon` uses. Every AppKit call is made on
the main thread; a failure makes `Show` return false, which is what `TrayIconService` already
treats as "no tray". Apple does not promise any of this from Catalyst, which is the price of
not shipping a separate AppKit bundle; a macOS release that breaks it degrades to no menu
bar icon rather than to a crash. The balloon's equivalent is a Notification Center post,
permission for which is asked the first time there is something to say.

`ITrayIcon` renders labels and reports which one was clicked, and knows nothing about
drives. Deciding what the menu says and what a click means is `TrayIconService`'s job in
the presentation layer, which is the only layer that can open a DI scope and reach a use
case — the same split as `IDriveMonitor` and `DriveWatchdog`.

Windows implementations carry `[SupportedOSPlatform("windows")]` and compile on both
heads; macOS implementations are wrapped in `#if MACCATALYST` because they reference
Apple BCL types that only exist on that target framework. `Platform/MacBundle` resolves
the running `.app` bundle for the two shortcut services.


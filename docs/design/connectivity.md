# Connecting, reconnecting and the watchdog

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Reconnecting away from the NAS

`ReconnectDrive` asks `IDriveRouter` — and through it `IHostReachability` — whether the NAS
answers before it tries to mount anything, and returns `DriveErrors.HostUnreachable` when it does not. The point is
that a drive whose host is absent has not failed to connect: on the NAS's own network a
bad mount fails in milliseconds, but away from it every attempt waits out the platform's
SMB timeout and files a warning naming a share that was never going to answer.

`HostReachability` opens a TCP connection to 445, then 139, rather than pinging — ICMP is
blocked or deprioritised often enough that a ping proves less than the connection Helix is
about to make. Readings are cached for ten seconds and the in-flight task is shared, so
thirteen shares of one pool cost one handshake per sweep rather than thirteen. **Anything
that is not the host declining to answer reads as reachable**, deliberately: a false
"unreachable" would stop Helix reconnecting a drive that would have come back, which is
the one thing the watchdog exists to do.

Both connectors also probe for themselves, before the gate and the mount, so a row
connected by hand and a batch get the same amber "Unreachable" as the watchdog. That is
what lets the **mount timeout be thirty seconds** rather than five. Neither `WNetAddConnection2`
nor `NetFSMountURLSync` takes a token: a timeout only abandons the thread the mount is on,
and the mount then finishes on its own. At five seconds a NAS addressed by name — a NetBIOS
or mDNS lookup and then the session — regularly came up a moment after its row had been told
"Connection timed out", and the other twelve drives, whose gate wait was capped at the same
five seconds, burst through ungated into the credential conflicts the gate exists to
prevent. Switching the drives back to the IP address "fixed" both, which is how it was
reported. The short timeout was only ever guarding against an absent host, and the probe
answers that in two seconds, once per host.

The per-host gate is held until the **mount itself** finishes, not until the caller has
been answered: a timed-out mount is still building the server session, and releasing the
gate at the timeout let the next drive in, whose conflict handling dropped the session the
first was creating. `RunWithTimeoutAsync` hands the underlying task back through
`onStarted` for exactly that, and logs what a timed-out mount eventually did. `HostSpelling`
caches answers, including "nothing", for the life of the process, but **not a lookup that
timed out** — that switched `ConnectByHostname` off for a host until restart because DNS
was slow at logon. A timeout is remembered for **one minute** instead, and that minute is not
optional: the lookup blocks its caller, `IsMountedFrom` is a caller, and `IsMountedFrom` runs
on the UI thread once per drive from the startup connect, "connect all" and the tray. Away
from home the reverse lookup of `192.168.1.6` never answers, so with nothing remembered every
one of those checks waited out the full 1.5s again, and the app hung at boot and after every
connect.

`IsMountedFrom` also does no lookup it can avoid. It answers "no" as soon as the letter's share
name is not the drive's — nothing under another share name can be this drive, whatever the
host is called — and it tries the home address and the away address as typed before it asks
for the other spelling. A drive mounted through its away address used to fall through to the
lookup on every check.

A **timed-out mount that then succeeds** is reconciled rather than left standing. The
caller has already been answered `DriveErrors.ConnectionTimedOut` while the mount is still
running, so what the user was told is wrong the moment it comes up: the tester screenshot
is thirteen connected drives under a "Connection timed out." banner. The abandoned task
therefore raises `INasConnector.MountSettledLate` with a `LateMountOutcome`, which
`MountReconciler` in the presentation layer turns into the three things that were missing —
the banner line is retracted, the drive is stamped through `MarkDriveConnected`, and the
row is told it is up. Only a *connect* reports itself this way (`reportsMount`); a late
disconnect or a late `Test` is logged and nothing more, because nothing was claimed about a
drive that a later success would contradict. A late mount that fails is not reported
either: the failure banner already says so.

`MarkDriveConnected` is addressed by **letter**, because a letter is all the connector
knows, and it refuses unless `IsLiveFrom` still says that letter points at that drive's
share — the mount may have been taken down again in the thirty seconds it took to arrive,
and a stamp is a claim about now. It writes no audit entry, for the same reason
`ConnectDrive` does not: the interceptor skips a save whose only change is
`LastConnectedOnUtc`.

The reconciler is subscribed for the length of a **session**, not a page: it starts at the
top of `HomePage.OnAppearing`, before the startup connect, since that batch is exactly what
produced the screenshot and its timeouts land while the page is still assembling. It stops
where `DriveWatchdog` stops — sign-out, in `AppShell` and `IdleLockService` — and not on an
idle lock, where the drives and the watchdog carry on.

The reconciler cannot fix the case where the late mount lands **before the batch has
answered**. The per-host gate serializes thirteen shares, so the first drive's timeout settles
while the other twelve are still mounting, `Notifier.Retract` finds no banner to take its line
out of, and the batch's aggregated failure (still carrying that line) is posted afterwards.
`DriveMountBatch.SettledLate` therefore re-checks every `ConnectionTimedOut` against
`IsLiveFrom` before the failure list is composed, in the batch and in `ConnectAllDrives`, and
a drive that has since come up counts as connected.

The row says which of the two it is. `DriveDisplay.OfflineReason` carries
`HostUnreachable` or `Refused`, and the status pill has one for each — amber "Unreachable"
against red "Failed" — beside the plain "Disconnected" that a drive the user put down
keeps. Without it, a laptop away from its NAS showed thirteen identical red pills, which
is also what thirteen wrong passwords look like. The reason is only ever set from an
attempt that actually happened, by `DriveWatchdog` through `DriveAttemptFailedMessage` or
by the row's own connect; a drive nobody has tried stays on the plain pill rather than
being guessed at, and a drive that comes up clears it, so a new outage cannot be blamed on
the old one. The unreachable pill gets the localized sentence and the refused one the
share's own words, since the domain's error descriptions are not translated.

`DriveWatchdog` reads that error code and puts the drive back at a flat 30-second interval
without counting it as a failure, so a laptop returning to the NAS's network reconnects in
seconds rather than at whatever the exponential backoff had escalated to.
A `NotFound` from `ReconnectDrive` is terminal — the drive was deleted while queued — and
`RefreshWatchedDrivesAsync` prunes the pending queue of drives that are gone or have their
own auto-connect off, since otherwise a deleted drive was retried and logged as a warning
every five minutes for the session. Drops are handled **in parallel**; the connector's gate
serializes the mounts anyway, and one hung share used to hold every other drive's drop
record and pill behind it. `DriveAttemptFailedMessage` is applied by `HomeViewModel` to the
master list, for the same reason `DriveUpdatedMessage` is: a row scrolled out of the
recycling list or hidden by the filter has no template to hear it. It logs those at
Debug rather than Warning — on a laptop this is the ordinary state of affairs for most of
the day, and a warning per drive per sweep buries the real failures in the file the user is
asked to send on. The sweep is skipped outright when `Connectivity` reports no network.

## Waking a NAS that is asleep

A drive can carry the **hardware address** of its NAS (`Drive.MacAddress`), and when it does,
a host that does not answer is sent a Wake-on-LAN packet before Helix reports it unreachable.
The unreachable branch used to be a dead end — the watchdog waited politely at thirty seconds
a try for somebody to walk over and press the NAS's power button. Now it is the thing that
fixes it: the packet goes out, the drive stays on that flat thirty-second retry, and the
first retry after the NAS has booted connects it.

The wake is sent by `IDriveRouter`, which is the one thing that decides a host is
unreachable, and both paths go through it: `ReconnectDrive`'s own gate, which answers before
the connector is reached, and the connectors' `WhenReachableAsync`, which is behind every
deliberate connect — a row, a group, a selection, "connect all" and the startup batch. Either
on its own would leave half the paths unable to wake anything. It is **not sent at all** when
the drive is pinned to a home network and the laptop is somewhere else, deliberate connect or
not, so a laptop at a café does not broadcast for a NAS it is nowhere near — a wake-up packet
does not leave the local network, so it could not have reached it anyway.

`IWakeOnLan.TryWakeAsync` holds a **five-minute quiet period per address**, which is what
makes calling it from two places, and from thirteen shares of one NAS, harmless: a pool of
thirteen drives sends one packet, not thirteen, and a NAS that is genuinely gone is not
broadcast at every thirty seconds all day. The quiet period starts only on a packet that
actually went out: a send that failed — the nudge after a resume can run before the adapters
are back — used to start it anyway and hold off every wake for five minutes. `WakeNowAsync` skips it, and is only used by the
explicit **Wake NAS** action (the row's menu, Ctrl+W, `--wake`) — a user pressing a button
has already decided it is worth sending. Addresses are normalized first
(`MacAddresses.Normalize`, `1a-2b-3c-4d-5e-6f`), so two spellings of one NAS share a quiet
period.

The packet is the standard six `FF`s and sixteen copies of the address, to UDP 9 and 7, both
to the limited broadcast and to **every up adapter's subnet broadcast**. The limited
broadcast alone only leaves by the default route, which on a laptop with a VPN or a Hyper-V
switch is often not the adapter the NAS is on.

Nobody knows their NAS's hardware address, so the drive sheets offer **Look up**, which
resolves the host and asks ARP (`SendARP`, the same call `WindowsNetworkLocation` uses for
the gateway). That only answers on the NAS's own network while it is awake, which is exactly
when the user is adding it — and the error says so rather than just failing. macOS has no
lookup (`IWakeOnLan.CanLookUp` is false and the button is hidden); the address can still be
typed and waking works on both heads. An address that does not parse is refused by
`CreateDrive`/`UpdateDrive` (`DriveErrors.NotAMacAddress`) but **dropped** on import: a vault
is restored whole or not at all, and a bad optional field is not worth losing the drive over.
The address travels in the export.

## Coming back from sleep

`DriveWatchdog` used to have exactly one trigger, its own five-second sweep, and that is
exactly wrong after a resume: every SMB session died with the lid closed, and the backoff
queue was still sitting on whatever delay it had escalated to — five minutes, commonly. Two
things now **nudge** it: `Connectivity.ConnectivityChanged` reporting access back, and the
sweep itself noticing that **more than a minute passed between five-second ticks**. The second
is how resume is detected, deliberately, rather than through `SystemEvents.PowerModeChanged`:
it needs no package, no eighth platform seam and works on both heads, and the only other thing
that stalls a timer for a minute is a machine so overloaded that re-checking the drives is the
right response anyway. The gap is measured from the **end** of the previous sweep, not its
start: a sweep that sat through a few thirty-second mount timeouts ran past a minute on its
own and was taken for a resume.

"Access back" means a **transition from no access**, not every event `ConnectivityChanged`
raises. Windows raises one for a VPN or a virtual adapter coming and going, and each of those
used to be a nudge — which remounts the auto-connect drives that are down, including the ones
the user had just disconnected by hand.

A nudge polls the monitor, brings **every pending retry forward to now**, and connects the
auto-connect drives that are down and not away from their home network, through the same
`ConnectDownDrivesAsync` the home-network arrival uses — arrival passes `pinnedOnly: true`,
a nudge does not. A drive that batch leaves down is put on the flat thirty-second retry rather
than forgotten: the batch's suppression re-seeds its letter as down, so the monitor will never
report it again, and a NAS still booting after the resume would otherwise wait for somebody to
press something. Nudges do not overlap: a resume that also brings the network back is one
recheck, not two. The ordinary sweep is skipped on the tick that detected the resume, since
the nudge has just done its work.


## Telling a deliberate disconnect from a drop

`IDriveMonitor.Suppress` hands back a handle; while it is held, the letters it names are
neither reported as changed nor used to move the baseline, and disposing it re-seeds them
from what is actually mounted. Every handler that mounts or unmounts on purpose —
`ConnectDrive`, `DisconnectDrive`, `ConnectAllDrives`, `DisconnectAllDrives`,
`ConnectDriveGroup`, and `DeleteDrive` for the mount it takes down — holds one
across the operation. **Add one to any new handler that mounts or unmounts.**

Without it the monitor cannot tell "the user pressed disconnect" from "the NAS fell over",
because both are a letter that stopped being there, and everything downstream believes the
second: the tray fires a toast per drive and, with auto-connect on, `DriveWatchdog` puts
every one of them straight back. Pressing "disconnect all" told the user seconds later
that their drives had reconnected. The same on the way up — the startup connect fired a
"reconnected" toast per drive, because the watchdog had seeded its baseline from a machine
with nothing mounted moments earlier.

The handle covers the whole operation rather than being a note filed after it: a poll
landing between the unmount and the note is the case that produces the spurious reconnect,
so there must be no window at all. Suppressions are counted, so a group going down while a
row's own disconnect is in flight cannot uncover the other's letters.

Releasing a suppression also raises `IDriveMonitor.TakenDown` for every letter that was up
before the operation and is down after it, and `DriveWatchdog` drops its pending retry for
those letters. A drive that had dropped and was still in the backoff queue when the user
connected and then disconnected it by hand used to be remounted at its next retry, with a
"reconnected" toast. A connect that merely failed is down before and after, so it is not
reported and the retries carry on.

`DisconnectDrive` refuses, with `DriveErrors.LetterInUse`, a letter that is not mounted from
the drive's own share, and `DeleteDrive` only cancels a persistent mapping that is down when
nothing else holds the letter: the connector's unmount is a forced cancel of whatever is on
that letter, and a row whose letter had been taken by another mapping offered to pull it out
from under whoever made it.

`ReconnectDrive` deliberately does **not** suppress. A drive coming back unattended is
exactly what the tray notification and the audit entry exist to report.


## A drive's home network

A drive can be pinned to the network it lives on — `Drive.HomeNetworkId`, with the name it
had when it was pinned in `HomeNetworkName` for the switch and the pill to show. The
unattended passes then leave it alone anywhere else: `ReconnectDrive` answers
`DriveErrors.AwayFromHomeNetwork` **before** it probes the host, and the startup
`ConnectAllDrives(OnlyAutoConnect)` drops it from the batch. Before this a laptop off its
NAS's network probed every share every thirty seconds all day and showed a row of amber
pills for a state that is not a fault; now the row says "Away", in neutral grey, and nothing
is attempted. Anything the user presses — the row, a group, a selection, "connect all" —
ignores the pin, for the same reason those ignore `AutoConnect`.

`INetworkLocation` is the seventh platform seam. On Windows a network is identified by the
**hardware address of the default gateway**: the adapter `GetBestInterface` would route the
internet through, its IPv4 gateway, and `SendARP` for that gateway's MAC. Not the SSID —
reading it needs location permission on current Windows, it misses a wired dock entirely,
and two cafés called "Guest" are not the same place. Not the gateway's IP either, since half
the routers in the world are `192.168.1.1`; that is only the fallback when ARP will not
answer. The connection profile's name is read for display and nothing else. Readings are
cached for ten seconds with the in-flight task shared, like `HostReachability`, because
every reconnect of a pinned drive asks.

The pin travels in the export, id and name both. The id is the gateway's hardware address,
which is just as true on another machine on the same LAN; a restore used to unpin every
drive, and they went back to probing the NAS from everywhere.

When the adapter Windows would route through has **no IPv4 gateway** — a full-tunnel VPN,
commonly — the network is read from the first adapter that has one instead. Reading the VPN
adapter found nothing, which reads as home, and a laptop at a café probed and woke its
pinned drives.

A network that **cannot be identified reads as home**, never as away. A false "away" would
stop a drive reconnecting that would have come back — the same asymmetry
`HostReachability` is built on. That includes a reading that fell back to `gateway-ip:`
for a drive pinned by `gateway:<mac>`: ARP missing its first answer just after the adapter
comes up is ordinary, and comparing the two kinds as strings read every pinned drive as
"Away" on its own network until a later reading got the hardware address.
`Drive.IsAwayFrom` treats that pair as unknown.

On macOS (`MacNetworkLocation`) the same identity is read by running `/sbin/route -n get
default` and `/usr/sbin/arp -n <gateway>` — no shell, `ArgumentList`, a two-second timeout
that kills the process tree. There is no `GetBestInterface`/`SendARP` to P/Invoke into that
is any simpler, and both tools are on every Mac. `arp` prints octets without their leading
zero (`0:11:32:a:b:c`), so they are padded before the id is built, and the id has exactly
the Windows form (`gateway:<mac>`, `gateway-ip:<ip>`) so a pin exported on one OS matches on
the other. The name shown is the interface (`en0`): the SSID needs location permission, as
on Windows.

`DriveWatchdog` reads the network on every retry sweep, and when it **changes** to a known
one it connects the auto-connect drives pinned there that are down, through `ConnectDrives`.
That is what makes "Office" come up on arriving at the office: the monitor only reports
letters that went away, so a drive that was never mounted this session would otherwise wait
for somebody to press something. The first reading of a session is only a baseline — the
startup connect has just handled that network. An `AwayFromHomeNetwork` reconnect is
treated like `HostUnreachable`: a flat thirty-second retry, logged at Debug, not counted as
a failure.

## A second address for away from home

A drive can carry an **address for away from home** (`Drive.RemoteHost`) — usually what a VPN
or Tailscale gives the NAS. Pinning a drive to its home network made a laptop stop hammering a
NAS it could not reach, but "Away" was then a dead end for everybody who *can* reach their NAS
from elsewhere, just not at `192.168.1.6`.

Which address a connect uses is decided in one place, `IDriveRouter`, and every mount and
test goes through it — the connectors' `WhenReachableAsync` and `ReconnectDrive`'s own gate.
It is not a platform seam: one implementation (`Infrastructure/Connector/DriveRouter`) covers
both heads, registered outside `AddPlatformServices()`, like `IHostReachability`, which it is
built on. The rule:

- **Pinned, and away from the home network:** straight to the away address. The home address
  is not probed — the pin already answered that — and no wake-up packet is sent.
- **Otherwise** (on the home network, unpinned, or a network that cannot be identified):
  the home address is preferred, and the away address used only if home is silent. Both are
  probed **at once**, so falling back costs one probe timeout rather than two in a row; the
  extra probe when home answers is one cached TCP connect. An unpinned drive gets the
  fallback too.
- Neither answers: `DriveErrors.HostUnreachable`, naming the address that was the last hope —
  the away one when away, the home one otherwise — so the watchdog's flat 30-second retry and
  the amber pill carry on exactly as before.

A drive with an away address is therefore **never** "Away": `ReconnectDrive` only answers
`AwayFromHomeNetwork` for a pinned drive that has none, and the startup
`ConnectAllDrives(OnlyAutoConnect)` and the watchdog's `ConnectDownDrivesAsync` keep it in the
batch rather than dropping it.

The away address is its own **server name string**, so on Windows it is its own credential
context and its own per-host gate — the gate is keyed by the address actually used, not
`Drive.Host`. That is the same property `TryAlternateSpelling` relies on, and it means a drive
coming up over the VPN never collides with a leftover session under the LAN address.
`ConnectByHostname` applies to the home address only; the away address is used as typed.

`IsMountedFrom` and `HasOtherMountsOn` recognise a mount under the away address as the
drive's own, or disconnecting it would have been refused as `LetterInUse`. Editing the away
address of a drive mounted **through** it unmounts it first (`UpdateDrive`), the same as
changing its share, for the same reason — the letter would otherwise point at an address
nothing in Helix claims. A drive mounted through its home address is left alone.

What it does **not** do: move a drive back to the home address on arriving home. A mount made
over the VPN keeps working while the VPN is up, and switching means unmounting a share
somebody may have a file open on. When the VPN drops, the drive drops, and the reconnect that
follows takes the home address.

`DiagnoseDrive` follows the same choice: when the home address is silent and the away one
answers, the report is about the away address — that is what a connect would have used.

The address travels in the export and is dropped rather than refused on import when it does not
parse, like the hardware address. Duplicating a drive copies it. It is not a credential and is
not tied to the home-network pin: a drive can have one without the other.


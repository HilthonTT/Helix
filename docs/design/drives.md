# Managing drives: groups, selections, import, protocols

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Drive groups

A `DriveGroup` is a named set of drives — "Home", "Office" — connected or disconnected in
one action from the dashboard strip, the group manager sheet or the tray menu.
`ConnectDriveGroup` does both directions, because everything around the mount is identical
either way and only the call in the middle differs. A drive's own `AutoConnect` flag does
**not** apply: that flag holds a drive back from the *unattended* passes, and pressing a
group button is as deliberate as it gets.

Membership is a list of drive ids on the group, stored as a **primitive collection** (one
JSON column) rather than a join table to `Drives`. A group does not own its drives: several
groups may name the same one, and deleting a drive must not delete the groups it was in.
Readers resolve the ids against the drives that exist, so an id left behind by a deleted
drive disappears from the group rather than becoming an error — `DeleteDrive` prunes them
anyway, to stop a long-lived install accumulating ids that name nothing.

## Acting on several drives at once

Everything used to be all-or-nothing — every drive, or one row at a time — with saved
groups as the only way to act on a subset. A group is something you set up in advance for a
set you use repeatedly; "these three, now" had no answer that did not involve creating a
group and leaving it behind. Rows carry a tick, and `ConnectDrives` takes the ids.

`ConnectDrives` and `ConnectDriveGroup` are the same work reached two ways, so everything
below the list of drives lives in `Core/Drives/DriveMountBatch`: which of the set are in the
wrong state, one suppression covering the whole batch, the mounts in parallel, the stamps
on one thread, the failures aggregated into a single error. What stays in each handler is
how the list was arrived at. The batch is a static helper rather than a handler because it
does **no authorization and no lookup** — the caller has already established that these
drives exist and belong to the signed-in user, and nothing that has not done so may reach
it.

Like a group, a selection ignores each drive's own `AutoConnect`: that flag holds a drive
back from what Helix does unattended, and ticking a row is as deliberate as it gets. Ids
that name nothing are skipped rather than failing the batch — a drive can be deleted from
another window while a selection is held — and the ids are de-duplicated, since a repeated
one would otherwise be mounted twice.

The selection bar **replaces** the all-drives chips rather than sitting beside them. Two
pairs of Connect/Disconnect on one line, one meaning "every drive" and the other "the three
you ticked", is the kind of ambiguity that gets a NAS unmounted by accident; for the same
reason the surviving chips now say "Connect all" and "Disconnect all" rather than "Connect"
and "Disconnect". The selection is deliberately **not** cleared after an action: connecting
three drives and then wanting to disconnect the same three is the common second act.

Unmounting is confirmed, in a dialog — the one thing on the page that is a question rather
than a result. Deleting a single drive was confirmed and unmounting thirteen at a stroke
was not, which had the risk backwards: an unmount pulls the filesystem out from under
whatever has a file open on it. The confirmation is skipped when nothing would actually
come down, because a confirmation for a no-op teaches the user to dismiss them unread, and
it has a singular and a plural form rather than one string with a number in it. A group's
disconnect, from the strip or the sidebar, is confirmed the same way through
`Common/DisconnectConfirmation`, counting the members `IsMountedFrom` reports. The tray's
own "disconnect all" is not confirmed and should not be: there is no window to put a dialog
on when it is used.

The question names the letters that still have something **open** on them, from
`INasConnector.CountOpenHandles` — on Windows `NetUseGetInfo`'s `ui1_refcount`, the same count
`net use /delete` warns from, which covers files, directories and an Explorer window's change
notifications. It is a count, not a list of programs, and deliberately so: the only way to name
the process is to enumerate every handle on the system and resolve each one's path, which needs
access to other processes, hangs on some handle types, and on a share means asking the NAS. A
**single row's** disconnect asks only when its letter is in use — a confirmation for every row
would be the no-op question that teaches people to dismiss them — and the count is taken off the
UI thread. macOS returns null (unknown), which leaves the question as it was.


## Importing over live mappings

`ImportDrives` and `CreateDrive` refuse a letter the connector reports as mounted — a USB
stick, an optical drive, another account's mapping would save fine and then fail at every
connect. The exception is a letter mounted **from the share being described**, which
`INasConnector.IsMountedFrom` answers: on Windows by comparing what `WNetGetConnection`
reports the letter points at with `\\host\share` under either spelling of the host, on
macOS by the volume being under Helix's own mount root. That is the normal state of a
machine a backup is restored on — persistent mappings survive a reinstall and live ones
survive an update, so the drives are up before the records that describe them are back —
and treating them as taken meant a vault of thirteen drives imported nothing until every
share had been disconnected by hand. A vault that ends up importing nothing is reported as
`JsonErrors.NothingToImport` rather than announced as a success.

## What the vault carries

A vault is a `VaultContents` — `Version`, the drives, the groups and the schedules — rather than
the bare drive list it used to be, and `ImportDrives` still reads that list (a JSON array is
version 1). Groups and schedules could not travel as they are stored, because both are keyed by
ids that mean nothing on another machine: a group carries its members' **letters**, a schedule
its group's **name** (`null` for every drive), and import maps them back onto the drives and
groups that exist after the drives are in. That is what makes a group naming a drive the
machine already had point at that drive, rather than at nothing.

A group whose name is taken (case-insensitively, as `IsNameUniqueAsync` compares) is skipped,
not merged; a schedule identical to an existing one, or naming a group that is in neither the
vault nor the database, is skipped; nothing new at all is still `NothingToImport`. A vault with a
higher `Version` than the build knows is `VaultUnsupportedVersion` — and an **older** build
reads a version-2 vault as invalid drive data, which is the price of an object at the top level.
Schedules keep their switched-off state; their run and edit stamps are not carried, so an
imported schedule waits for its next occurrence like any newly saved one.

`INasConnector` answers two different questions about a letter, and they must not be
confused. `IsMountedFrom` is "this letter **points at** this drive's share", and on Windows
that includes a **remembered** persistent mapping that is not connected — `WNetGetConnection`
answers `ERROR_CONNECTION_UNAVAIL` with the remote name when a restore at logon failed because
the NAS was asleep or the laptop was elsewhere. `IsLiveFrom` is "this drive **is up**", and
excludes it. Deciding which to ask is deciding whether the remembered entry matters:

- **Is the drive up** — `IsLiveFrom`. `DriveMountBatch.IsUp` (connect all, a group, a
  selection, the startup connect, the low-space check), `DiagnoseDrive`'s share step,
  `MarkDriveConnected`, the watchdog's `ConnectDownDrivesAsync`, `--status` and the tray's
  folder menu. Asking `IsMountedFrom` here was the bug: a persistent drive whose restore
  failed at logon read as up everywhere, so nothing ever connected it again, and the
  diagnosis reported it "already mounted" and skipped the credential test.
- **Does the letter belong to this share** — `IsMountedFrom`. `DisconnectDrive`, "disconnect
  all" (`DriveMountBatch.IsMapped`), `DeleteDrive`, `UpdateDrive`'s unmount before a share
  change, and the letter-in-use checks in `CreateDrive`, `CreateDrives` and `ImportDrives`.
  Cancelling a remembered entry is exactly what a disconnect should do, and a remembered
  mapping of the same share is the restore-after-reinstall case, not a conflict.

The same split applies to the letter sets. `GetConnectedLetters` is every letter present,
remembered mappings included, and answers "is this letter **taken**" — the letter pickers and
the letter-in-use checks. `GetLiveLetters` leaves out a network letter whose mapping is only
remembered, and is what `DriveMonitor` polls: a persistent drive whose server went away keeps
its letter, and reading presence as "up" meant its drop was never reported and the watchdog
never brought it back. The dashboard rows, counts, donut and storage total, the sidebar and the
tray all ask `GetLiveLetters` and then `IsLiveFrom` per drive (`DriveDisplay.IsUp`), so a USB
stick on a drive's letter is not the drive being connected; "disconnect all" confirms against
`DriveDisplay.IsMapped`, the letters it will actually take down.

`Drive.Persistent` is fixed when a mapping is made, so `UpdateDrive` re-makes the mapping
when the switch moves on a mounted drive — but only when `CountOpenHandles` says nothing is
open on it; with files open, or where the count is unknown (macOS, which ignores the flag
anyway), it applies at the next connect. A mapping that is only remembered is cancelled.

`HasOtherMountsOn` only counts **live** mappings, since a remembered one holds no session
to inherit. It also asks `HostSpelling` for the other spelling only once it has found a
network letter that neither the home nor the away address explains, because that lookup
can block for its full 1.5s.

Mounting onto a letter that holds a remembered entry is refused by `WNetAddConnection2`
(`ERROR_ALREADY_ASSIGNED`, or `ERROR_DEVICE_ALREADY_REMEMBERED` with
`CONNECT_UPDATE_PROFILE`). `Connect` then cancels that entry, unforced and only when it is
**this drive's share and not connected**, and tries once more. An `ERROR_ALREADY_ASSIGNED`
on a letter that is already **live** from this share is answered as success: it is what two
overlapping batches (the startup connect and a "connect all" pressed during it) produce,
and the drive is up.

A letter merely present on the machine is not up either: a USB stick that landed on `E:` used to make
"connect all" skip the NAS drive defined there, "disconnect all" try to unmount the stick,
and the low-space check measure it under the drive's name. `DeleteDrive` unmounts whatever
is mounted from the share, persistent or not, and refuses the delete if that fails, since a
record deleted out from under a live mapping leaves a letter nothing in Helix can take down.


## Adding drives from what is already there

**Browsing a server's shares.** The add-drive sheet lists what the server shares, given its
host and credentials, and a share ticked there fills the name; ticking several creates one
drive per share through `CreateDrives`, the first on the chosen letter and the rest on the
next free ones counting down, as the letter picker itself defaults to the last. Shares the
user already has as drives are shown with their letter and cannot be ticked. `ListShares`
goes through `INasConnector.ListSharesAsync`, which on Windows is `NetShareEnum` at level 1,
keeping disk shares that are neither special nor `$`-suffixed.

How it authenticates is the part to leave alone. With a drive letter already mounted from the
server it enumerates on that session. Otherwise it connects `\\host\IPC$` with the typed
credentials first — **never** enumerates anonymously first, because an unauthenticated
`NetShareEnum` signs in as the Windows account and leaves exactly the session that turns the
next mount into `ERROR_SESSION_CREDENTIAL_CONFLICT`. A 1219 on that `IPC$` connection takes
the same leftover-session branch as a mount (drop it unforced, try again), and a conflict
that survives the drop lists the shares on the session that holds the server, because a list
of names is not a claim about the credentials. It holds the per-host gate like a mount does.
macOS has no equivalent and the button is hidden (`DrivePlatform.SupportsShareBrowsing`).

**Duplicate** — the row's right-click menu or Ctrl+Shift+D — opens the add sheet with the
drive's server, credentials, switches and home network filled in and the share left blank:
the second drive on a NAS is the common case and it is everything but the share. It is the
natural partner of the browser.

**Adopting existing mappings** lists the letters mapped on this machine that no Helix drive
claims (`GetUnmanagedMappings` over `INasConnector.GetMappedShares`, which on Windows asks
`WNetGetConnection` for every letter, remembered-but-unavailable ones included) and turns
the ticked ones into drives with one set of credentials. They are created persistent,
because a mapping someone made in Explorer almost always was. `CreateDrives` accepts a letter
that is mounted from the share it describes, the same exception `CreateDrive` makes, so
adopting does not require unmapping first. WebDAV mappings (`\\server@SSL\...`) are left
out; `Drive.Host` cannot hold them.

`CreateDrives` is all-or-nothing: every letter is checked — unique within the batch, not
already a drive, not held by something else — before anything is inserted, so a batch that
fails leaves no half of itself behind.

## One server, one set of credentials

Each drive still stores its own username and password, but editing them on a drive whose
server has other drives offers to apply them to those too (`UpdateDrive.Request
.ApplyCredentialsToServer`). The switch appears only once the credentials have actually
changed and only when there is a sibling; it is **off by default**, because a NAS commonly
carries an admin share and a guest share under different accounts, and changing the other
drives silently is worse than one more click. "Same server" is `Drive.IsOnSameServerAs`:
the host string, case-insensitive — two spellings of one NAS are two servers here, which
errs towards changing too little.


## Protocols other than SMB

A drive is SMB unless `Drive.Protocol` says **WebDAV** or **NFS**. `Drive.Name` is then a path
rather than a share name (`home`, `remote.php/dav/files/me`, `/volume1/media`), and
`DriveProtocols.NormalizePath` is how both connectors read it: slashes either way, none at the
ends. `Drive.Port` is kept for WebDAV only, and dropped when it is 443, so a drive typed with and
without the default port is one drive; `UsePort` re-applies that whenever the protocol changes.
The migration defaults every existing row to SMB.

Both protocols go through the **same mount APIs** as SMB rather than a client of Helix's own.
On Windows, `WNetAddConnection2` takes an explicit `lpProvider` — `"Web Client Network"` for
WebDAV, `"NFS Network"` for NFS — and the remote name is built by
`WindowsNasConnector.RemoteFor`: `\\host@SSL[@port]\path` for WebDAV (always HTTPS: the WebClient
service refuses basic authentication over plain HTTP, and a NAS password is not something to send
in the clear anyway; an empty path is `DavWWWRoot`), `\\host\volume1\media` for NFS. On macOS
`NetFSMountURLSync` takes an `https://` or `nfs://` URL (`MacNasConnector.UrlFor`). The mount
point, the letter, persistence, the monitor, suppression and the storage probe are all the
letter's business and did not have to change. A missing Windows feature — Client for NFS turned
off, the WebClient service stopped — comes back as `ERROR_BAD_PROVIDER`, and is reported as
that rather than as the generic Win32 sentence.

**NFS carries no credentials.** `DriveProtocols.UsesCredentials` is false for it, the entity
accepts empty ones, the sheets hide the fields and the connectors pass null; NFS
authorization is by host and UID on the server, and a stored password would be a field that
does nothing. "Apply these credentials to the server" skips NFS siblings for the same reason.

Everything SMB-specific stays SMB-only, and `Drive.Protocol` is the guard: the 1219
credential-conflict branches, `TryAlternateSpelling`, `ConnectByHostname`, `HasOtherMountsOn`
(answers false, so the diagnosis never claims a password went unchecked), share browsing and
adopting existing mappings. Reachability is not: `IHostReachability`, `IHostDiagnostics` and
`IDriveRouter` take the ports from `DriveProtocols.ProbePortsFor` — 2049 for NFS, the WebDAV
port or 443 — because a NAS that serves NFS with SMB switched off would otherwise read as
unreachable forever. SMB passes `null`, which is the 445-then-139 it always probed, and the
reachability cache is keyed by host **and** ports so an SMB reading never answers for WebDAV.

The protocol and port travel in the vault. An unknown protocol makes the row invalid rather
than silently mounting it as SMB; an older build reads a WebDAV drive as SMB and an NFS drive
as invalid (no credentials), which is the price of an additive field.

**SFTP is not offered and should not be added this way.** Neither OS mounts it natively: it
needs WinFsp + SSHFS on Windows and macFUSE on macOS, both third-party kernel drivers that
Helix would have to detect, version-check and explain, and neither goes through the mount
APIs everything above relies on. **Mounting to a folder** rather than a letter on Windows was
also left out: `WNetAddConnection2` only maps devices, a folder mount means a directory
symlink to a UNC path (which needs Developer Mode or elevation), and the letter is the
identity the monitor, suppression, the audit log, the command line and the vault all key on.


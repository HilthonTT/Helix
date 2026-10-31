# Drive diagnosis and reliability

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Diagnosing a drive that will not connect

Everything above is what Helix knows about *why* a mount failed, and until now all of it
collapsed into one pill and one banner: a drive that is off, a drive whose share was
renamed, a drive whose password is wrong and a drive whose server is held by somebody
else's session all read as "Failed". The stethoscope chip on the row runs `DiagnoseDrive`
and reports the chain step by step, so the user can answer for themselves the question
that otherwise becomes an email.

Four steps, in this order, and the order is the narrative: the server's **name**, then
**reaching** it, then the **share and credentials**, then the **drive letter**.

- **Name.** `IHostDiagnostics` asks `HostSpelling` for the host's other spelling and says
  what it found. A miss is a warning, not a failure — a home router with no PTR records
  for its leases is the normal case, and Helix mounts by what was typed regardless.
- **Reaching it.** A fresh TCP probe of 445 then 139, and it names the port that answered.
  Deliberately **not** `IHostReachability`: that one caches for ten seconds and shares its
  in-flight task so a sweep of thirteen shares costs one handshake, which is exactly wrong
  for a check the user just pressed a button to run. A failure here stops the chain and
  the share step is reported as **skipped** rather than guessed at.
- **Share and credentials.** `INasConnector.TestAsync`, whose deviceless mount never
  touches the drive's own letter. Three outcomes rather than two, and the third is the
  point: **`SessionConflict`** is now a distinct error code from the Windows connector
  rather than a sentence inside `FailedToConnect`, so "the password is wrong" and "Windows
  is signed in to this server as somebody else" stop looking identical.
- **Letter.** Always runs, even when the server never answered — a letter taken by a USB
  stick is worth knowing about while the NAS is off. It tells "already mounted from this
  share" apart from "in use by something else", which are the same letter and opposite
  problems.

The step this exists for is the **warning** on a share that tested fine.
`INasConnector.HasOtherMountsOn` answers whether another letter is already mounted from
this drive's server; when it is, Windows reuses that credential context and the stored
password is never actually checked, so the check reports success **and says so**. That is
the limit documented above — "editing a password and reconnecting that one drive will
appear to work whatever you type" — finally said out loud at the moment it is misleading
somebody. On macOS `HasOtherMountsOn` is `false` and means it: NetFS authenticates per
mount, so there is no session to inherit and nothing to warn about.

A share Helix already has mounted is reported as such and **not tested again**. Testing it
would either join its own session or conflict with it, and both answers would be about the
test rather than about the drive.

`IHostDiagnostics` is **not** a platform seam. Like `IFileBrowser`, it has one
implementation for both heads and is registered outside `AddPlatformServices()` — a TCP
connect and a DNS lookup are the same on either OS.

Nothing here composes a sentence. Each step carries a `DiagnosticStep`, a
`DiagnosticOutcome`, a `DiagnosticFinding` and at most one piece of data — a resolved name,
a port, a letter — and `DiagnosticStepDisplay` builds the sentence from `AppResources` at
display time, in the user's language, the same way `AuditlogDisplay` does. The one
exception is a rejected credential, which shows the connector's own words, because the
domain's error descriptions are not translated and paraphrasing "The network name cannot be
found" into something vaguer would lose the only part that identifies the problem.

The report is a modal sheet, which is not a contradiction of the no-alerts rule: that rule
is about announcing the *outcome of work the user asked for and watched happen*. This is a
document the user opened, reads, and closes — and it is too long to be a banner.

Below the steps the sheet shows the drive's **last 30 days** from `GetDriveReliability`, which
reads the audit rows `ReconnectDrive` already writes: each `DriveDisconnected` is a drop, a
`DriveReconnected` that follows one is a recovery (one with no drop before it — a network
arrival, a resume nudge — is not counted), and the typical recovery is the **median**, because
an app closed overnight turns one drop into a ten-hour outage. There is no uptime percentage:
`ConnectDrive` writes no row, so a drive the user brought back by hand would read as down until
the next unattended reconnect, and the figure would be fiction. Three drops in a week marks the
drive with a warning. It is loaded beside the check, not after it, and a failure to read it
leaves the section out rather than failing the report.


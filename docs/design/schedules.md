# Schedules

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Schedules

A `Schedule` connects or disconnects a group, or every drive, at a time of day on chosen
weekdays — "connect *Office* at 07:00 on weekdays", "disconnect everything at 23:00". The
command line and Task Scheduler could always do this; next to nobody sets that up, and it
cannot run behind the idle lock. The sheet is under **Drives → Schedules** (Ctrl+T).

`RunDueSchedules` is the whole of the rule, and `ScheduleService` asks it every twenty
seconds for the length of a session. It starts beside `StorageAlertService` and stops where
that does — sign-out, not the idle lock — because a schedule is the user's standing
instruction, like the watchdog's reconnects, and not a keyboard the lock should stop.

The time is **wall-clock local time**, compared through `ILocalTimeZone`, which clears
`TimeZoneInfo`'s cache on every read: a laptop that changes time zone means 07:00 where it
now is. `Schedule.DueOccurrence` finds the latest occurrence at or before now and calls it
due when it is later than both the last run and the last edit, and **no more than ten
minutes old** (`RunDueSchedules.Grace`). The grace is the design, not a tolerance:

- An occurrence missed by more than that is **skipped, not run late.** A laptop opened at
  08:00 after sleeping through 23:00 must not pull every drive down the moment it wakes,
  and a "connect at 07:00" opened at 10:00 is what auto-connect is for anyway. Ten minutes
  still covers a resume a little after the hour and a sign-in at the time itself.
- A schedule saved **after** its time today waits for the next occurrence. Without the
  edit stamp as a floor, creating "23:00" at 23:05 fired it on the spot.

`LastRunOnUtc` is stamped and saved **before** the drives are touched, so a run that fails,
hangs or crashes the app is not retried every twenty seconds for the rest of its grace. A
failed run is reported, not repeated; the watchdog and the user own it from there.

The run itself is `DriveMountBatch`, the same work as a group or a selection — suppression,
parallel mounts, stamps, one aggregated failure. Like a group button it **ignores
`AutoConnect`**: the user named these drives when they wrote the schedule. It **does** honour
the home-network pin on a connect, as the startup connect does: a pinned drive with no away
address is left out when the laptop is elsewhere, because a 07:00 "connect Office" at home on
a Saturday would otherwise probe, and try to wake, a NAS across town. A disconnect ignores
the pin — a drive mounted through its away address should still come down at 23:00.

A schedule names a group by id, with a **cascading foreign key**: deleting a group deletes
the schedules that name it. That is the one place `DriveGroup` is referenced from another
table rather than the other way round; a schedule whose target has gone could only fail at
its next run. `ScheduleRepositoryTests` holds SQLite to that, since EF's in-memory cascade
would hide a missing `PRAGMA foreign_keys`.

Each run says what it did, through the tray balloon when the icon is up and the banner
otherwise — the `HotkeyService` rule. A scheduled disconnect that just happens, with nothing
said, reads as the NAS falling over. Schedules and groups travel in the vault with the drives
(see **What the vault carries**).


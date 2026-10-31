# Storage alerts, history and Insights

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## The low-space warning

`Settings.StorageAlertThresholdPercent` is free space, as a percentage of a volume, below
which Helix says something; 0 turns it off. A percentage rather than a byte figure because
one install commonly watches volumes orders of magnitude apart in size.

`GetStorageAlerts` measures through the same `IStorageProbe` the dashboard uses, so it
answers in **volumes, not drives** — thirteen shares of one pool are one thing running out
of room, and warning about it thirteen times would train the user to dismiss the warning.
`VolumeUsage` carries the letters that resolved to each volume so the warning can name the
drives. Only mounted drives are measured: an unmounted one has not run out of space, it
has not been asked.

`StorageAlertService` runs it on its own 15-minute timer — free space moves over days, and
each reading is a blocking call against a share — and remembers which volumes it has
already warned about, so a full pool is reported once rather than every quarter of an hour.
A volume that recovers is forgotten, so it can warn again months later.
"Recovers" means **measured and found fine**: `StorageAlertReport` carries every volume the
check measured alongside the alerts, and only those are eligible to be forgotten. A pool
whose drives were unmounted for one check, or whose probe ran past the timeout, is absent
from the alerts without having recovered, and forgetting it re-fired the warning. It starts a minute
after the dashboard does, because the drives are still being connected at that moment.

The same service records **storage history**: `RecordStorageSamples`, at most every six hours,
writes one `StorageSample` per volume per **UTC day** (the unique index is `(UserId, VolumeId,
Day)`; a later reading the same day updates the row) and prunes anything older than 400 days. It
probes on its own rather than reusing the alert check's reading, because the alert check returns
early when the threshold is 0 and history is wanted regardless; four probes a day is nothing.
A volume is its `VolumeId` — the total size, as the probe already keys it — so a volume that is
grown starts a new history, which is the honest answer.

`GetStorageForecasts` fits a least-squares line through the last 30 days of each volume
(`Core/Storage/StorageTrend`) and turns a falling one into days until full. It answers nothing
from fewer than three samples or less than a week of span, for a volume not measured in three
days, or past ten years. The dashboard's storage tile shows the **soonest** volume within a year
as one caption, with the volume's drives and its daily rate in the tooltip, and refreshes on
`StorageSamplesRecordedMessage`. It is a straight line on purpose: free space on a NAS moves in
steps (a backup, a deleted folder), and anything cleverer than a trend over a month would be
claiming a precision the data does not have.


## Insights

The **Insights** page (`Views/Insights`, route `insights`) shows two things Helix already
recorded and had nowhere to show.

**Storage over time** is `GetStorageHistory` over the `StorageSample` rows the alert service
writes: one series per volume, oldest first, for 30 days, 90 days or a year. The chart plots
**used percent**, not free bytes, so volumes orders of magnitude apart share one scale and a
volume that was grown reads as a step rather than a cliff. The caption's trend is fitted over
the **last 30 days** whatever range is shown, the same window `GetStorageForecasts` uses, so
the page and the dashboard tile cannot disagree about when a pool fills; a year of history is
for looking at, not for extrapolating from. Volumes filling soonest come first.

**Drive reliability** is `GetEstateReliability`: one audit-log read for the window rather than
a query per drive, grouped by drive id and summarized by `Core/Drives/ReliabilitySummary`,
which is the same code `GetDriveReliability` runs for the diagnosis sheet — so the page and the
sheet cannot count a drop differently. Only drives that dropped are listed; a list of thirteen
rows saying "no drops" buries the one that did.


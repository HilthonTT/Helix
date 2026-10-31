# Drive list, audit log list and filtering

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Finding a drive, and a row in the log

Both lists filter live, from a box that is always on screen, over what is already in
memory. Both used to be a modal: open a sheet, type, press Search, wait for a database
round trip, and have the results replace the list — four interactions and a query to narrow
thirteen rows. `SearchDrives` and `SearchAuditlogs` were deleted along with the sheets;
`GetDrives` already loads everything, so the drive query was narrowing a set the app was
holding anyway, and the history is loaded a page at a time with a search widening it (see
**Long lists**) rather than being asked a question the database cannot answer.

A drive letter is `GeneralValidation.IsDriveLetter`: one character, A to Z. `char.IsLetter`
let a hand-edited vault save `É` as a letter the edit modal could not even offer. Export and
import catch `IOException` — the folder picked is as likely as not a NAS share — and a vault
that adds nothing is `JsonErrors.NothingToImport`, not a success banner. EF Core's command
logging is filtered to Warning in `AddInfrastructure`, because every query at Information
rolled the 2 MB log over the reconnect lines it exists for.

Filtering in the viewmodel buys two things the queries could not have. The drive rows are
**reused** rather than rebuilt, so a tick, a mount in flight and a drive's offline reason
all survive typing — re-projecting fresh `DriveDisplay`s per keystroke would have thrown the
selection away on the first letter. And the audit log is matched against the **sentence the
row shows**, which is composed at display time in the user's language and does not exist in
the database: searching for "disconnected" used to find nothing while the page was full of
the word.

Each page keeps a master list (`_allDrives`, `_allAuditlogs`) and `Drives`/`Auditlogs` is
the filtered, sorted view onto it. **Anything that describes the estate rather than the
list reads the master**: the storage and connection tiles, the connectivity donut, group
membership, and the count "disconnect all" confirms against. A search box narrowing the
list below them is not the NAS getting smaller.

`RefreshTotalsAsync` is raised once per drive by a "connect all", each run probes the shares,
and the probes finish in whatever order they like — so the storage figure is only applied by
the **latest** request, or a reading taken before the last drive mounted could land last and
stay. A reading that **measured nothing while drives are connected** is not applied at all,
on the dashboard or in the sidebar: every share of one pool rides the same SMB session, so
when Windows re-establishes an idle one, or the NAS spins its disks up, all thirteen
measurements miss the three-second cap together and the probe returns no volumes. That used
to be shown as "0 TB" for a NAS that was plainly there, until some later refresh happened to
be quick. Only "no drive is connected" shows zero. The probe writes an Information line when
it measures nothing, since the per-drive failures are Debug and a release build drops them.
`DriveCreatedMessage` is ignored for a drive the master list already holds: a fetch that
was in flight when the drive was saved returns it too, and the duplicate row it left made the
next `FetchDrivesAsync` throw on its id lookup.

Rows filtered out are **deselected** on the way. Acting on a ticked row that is not on
screen is the one outcome worth ruling out — "disconnect" has to mean the rows the user can
see.

An empty list now has two states. Filtering thirteen drives down to none and being told
"you have no drives yet — add one" is the app forgetting what the user just typed, so
`ShowNoMatches` offers to clear the search and `ShowNoDrives` offers to add a drive.

Column headers sort. The list always drew a header row and never let anyone click it; the
only ordering was ascending or descending, chosen in the modal, with no way to say by what.
Letter, Name and Status sort; **storage usage does not**, because the column holds a
sentence built for reading ("1.2 TB free of 43.2 TB") and ordering drives by the text of
that puts 9 GB after 40 TB. A fresh column starts ascending rather than keeping the previous
direction, and the caret marks which column is live.

## The drive card at an ordinary window size

The screenshots that exposed this were taken at 150% display scaling, which is worth
remembering before reading pixel positions off one: a 1960px-wide window is a 1300 DIP
window, and after the sidebar and the 320 DIP connectivity column the drive card is about
670 DIP wide. Fixed row columns totalling 636 left the one flexible column — the drive's
**name** — with nothing, and the header's title, search box and three labelled chips wanted
about 810 in a 610 slot, so the search box got squeezed to a stub.

`HomeViewModel.IsCompact` is the answer, set by the page from the **card's own measured
width** (`DriveCard.SizeChanged`, threshold `CompactCardWidth`), not the window's — the
card is what has to fit. Compact hides the chip labels (each chip carries the same text as a
tooltip, so icon-only is still explained) and collapses the storage-usage column to 0.
Storage is the column that goes because for every drive that is down it reads "Drive not
ready", and the name is the column that matters.

The column width lives on `Common/DriveListLayout`, an observable singleton that the header
grid and every row grid bind with a **static `Source`** — never `RelativeSource`. A
`ColumnDefinition` is a `BindableObject` but not an `Element`: it has no place in the visual
tree and no parent, so an `AncestorType` lookup on it has nothing to walk and fails inside
the item template, which took every row with it — a list that counted thirteen drives and
drew none. A static-source binding needs only something to subscribe to and works on any
bindable object. `HomeViewModel.IsCompact` mirrors into the singleton for the labels that
bind through the viewmodel.

**The header grid on `HomePage` and the row grid in `DriveTemplate` must stay identical**
— same widths, same spacing — or the column labels drift off the data. The twelve-DIP
column gap beside the collapsed storage column is the one thing the binding cannot remove,
and is accepted.

## Long lists

Both lists are `CollectionView`, not `BindableLayout` over a `VerticalStackLayout` in a
`ScrollView`. That shape realizes every row before the page can be drawn, which is
survivable for thirteen drives and is not for ninety days of audit history — the default
retention. `SelectionMode` is `None` on both: the drive rows carry their own tick, and
CollectionView's own selection would also fire on a click anywhere in the row, so pressing
"Disconnect" would select the row as a side effect.

Rows are recycled, which `DriveTemplate` already handles — `OnBindingContextChanged`
unregisters before it re-registers, so a view handed a different drive does not keep the
old one's subscriptions.

**The drive rows are reachable from the keyboard**, through `Behaviors/RowFocus` — one tab
stop per row, not one per control. The row's status pills and icon chips are styled
`Border`s with tap gestures, which take no focus, and MAUI's `Button` takes text rather than
arbitrary content, so making each of them focusable would have meant rebuilding every
interactive part of the row *and* putting a hundred tab stops in front of a user with
thirteen drives. Focus lands on the row and the keys act on it, the way a file manager's
list does: **Enter** connects or disconnects, **Space** ticks, **Delete** deletes, **F2**
edits, **Ctrl+D** diagnoses, **Ctrl+Shift+D** duplicates, **Ctrl+O** opens the folder,
**Ctrl+W** wakes the NAS. Every one of them is the same work
the mouse reaches through a pill or a chip, routed through `IRowKeys` on `DriveTemplate`, so
a row does the same thing either way. Ctrl+O is refused rather than swallowed when the drive
is down, because the chip is not there either. The same actions are on the row's right-click
menu (`FlyoutBase.ContextFlyout`), which is also where Duplicate lives: a fifth chip would
have widened the actions column and taken the room from the drive's name.

Up and Down are answered by the behavior itself, with WinUI's `FocusManager.TryMoveFocus` —
moving between rows is the list's business, not the drive's, and this way nothing has to
find the `CollectionView`. The behavior is Windows-only like `Hover`; Mac Catalyst has no
equivalent seam in MAUI and the rows stay mouse-only there. The focus ring is a `Focused`
visual state on the `Row` style, and the style carries a **two-wide transparent stroke with
two less padding at rest** so the ring appearing cannot shift the row's columns off the
header grid they have to line up with.

`GetAuditlogs` is **paged** — `Skip`, `Take` and the sort order go to the database, and
`AuditlogPage` carries the total so the page can tell there is more behind it without asking
for it. Virtualizing the rows stopped the page *drawing* ninety days of history; it was still
reading all of it out of SQLite and holding it. The `CollectionView`'s
`RemainingItemsThresholdReached` pulls the next hundred as the user scrolls, and the composite
`(UserId, CreatedOnUtc)` index is what keeps a page from being a scan and a sort. The page
boundary breaks ties on the id, so rows written in the same tick cannot straddle it and be
handed over twice.

Paging is by `Skip`, and the history is **written to while it is read** — the watchdog files a
row per drop and per reconnect. Newest-first, a row written after the first page pushes every
later one down by one, so the next page began with the row the last one ended on and the list
showed it twice. `AuditlogsViewModel` keeps the ids it has loaded and drops repeats. The same
rows also mean the loaded count never reaches the total, so `HasMore` stayed true forever and
the search's load-the-rest loop had nothing bounding it; a page that adds **nothing new** now
marks the history exhausted. A page that arrives is **appended** to the bound collection when
what is shown is a prefix of what is wanted, rather than replacing it — a new `ItemsSource`
per page makes the `CollectionView` rebuild itself, which discards the scroll position the
user had just reached in order to ask for that page.

Searching is the one thing that cannot be paged, and it pulls the rest of the history in
instead. The sentence a row shows is composed at display time in the user's language and is
not in the database, so a search can only be answered over rows that are loaded — typing
therefore filters what is loaded immediately and widens behind that, once. The count beside
the title reads the **estate**: the whole history while the list is merely scrolled part of
the way through it, the matches once a search narrows it. Changing the sort order re-asks
from the top rather than re-sorting what is loaded, since sorting a hundred of the newest
rows ascending would present them as the oldest history.


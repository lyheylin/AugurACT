# Adding a mechanic

How to take a boss mechanic from "players know it exists" to "the plugin shows it per encounter and records
it per NPC". Barrage (`BarrageDetector`, `mechanics/barrage.js`) is the reference implementation to copy.

## 1. Find it in real logs

- **Ask the user for the shape of the mechanic and a known example**, including a known failure. Their
  examples save hours of searching, and failures are rare.
- **Learn the game rules the log can't show**, such as raid versus group differences. Record these in
  [Mechanics](MECHANICS.md) and in memory.
- **Prefilter the archive first.** The logs are tens of GB. Grep each file down to the lines the mechanic
  could involve, keeping the `eq2log_<Character>` file names (the CLI reads the logger's name from them) and
  the zone line (`] You have entered `), and run everything else on the filtered copies. Anything that needs
  the player list (`timeline`) must run on full logs, because the roster reads every line for ability use.
- **Write down the line sequence** for each content type, with exact text: warning, player response, effect,
  cancel. Raid and group versions usually differ.

## 2. Decide the outcome from the effect, not the response

- **The logger only sees their own group's responses.** Judge success by what the mechanic did (who took
  damage) and keep the responses as evidence.
- **Check the rule against the counter-cases** before trusting it: successes with no visible response, and
  failures despite one.
- **Check how often the "failure signal" happens anyway**, as a baseline across the whole fight.
- **No effect can mean success.** Some successes log nothing at all.
- **Keep "no outcome" as an outcome.** A boss can warn and never follow through; that's a result, not a
  detection bug.
- Confirm odd cases with the user rather than inventing rules.

## 3. Detector in Core

Copy `src/AugurACT.Core/Mechanics/BarrageDetector.cs` and `BarrageCast.cs`. A detector implements
`IMechanicDetector`:

- `MightMatter(rawLine)`: a cheap substring check. Only lines that pass it are parsed and fed, in the plugin
  and in the CLI's prefilter.
- `Feed(Eq2LogLine)` per line in time order. Expire open casts by time windows (lead time, hit window,
  late-response window) rather than waiting for the end.
- `Complete(EncounterSummary)`: add the results to the summary's list and return how many there were.
- The constructor takes a `ScanContext`: the logger's name, the zone, and the shared `PlayerRoster`, which says
  who the players are (ACT's ally list in the plugin, log evidence in the CLI). Don't work that out again.

Then register it, in two places in Core and nowhere else:

1. Add it to `MechanicSet.Create` (`src/AugurACT.Core/Mechanics/MechanicSet.cs`).
2. Add a `[DataMember]` list for its results to `EncounterSummary`, so they travel in the `analysis` message
   and the CLI page.

The plugin and the CLI `timeline` command then run it with no changes.

Rules that have held so far:

- Match the boss by its **exact NPC name prefix**. Names hold apostrophes and commas
  (`Evuntressa, Kryterion's Behest`), which the generic parser splits wrongly.
- Targets in emotes are upper case: compare without case. `YOU` is the logger.
- Keep the source lines on each result (stripped of markup), so the page can show why a verdict was made.
- Add a `ContentKind` aware variant if raid and group differ.

## 4. Tests

Synthetic fixtures only; real logs hold other players' names and chat. Cover each outcome for each content
type, the logger as the target, missing warning lines, player abilities with similar names, and catalog
de-duplication. See `tests/AugurACT.Core.Tests/BarrageDetectorTests.cs`. Feed through `EncounterSummary.Build`
when the detector needs the roster (see `DeathDetectorTests.cs`).

## 5. CLI

- `timeline <logs> --html page.html` writes the plugin's page as a standalone file, with every fight that has
  a mechanic in it. It runs all registered mechanics, so a new one needs nothing here. The page holds player
  names: never commit or share it (`.gitignore` covers `*timeline*.html`).
- For tuning, add a subcommand of your own (`src/AugurACT.Cli/Commands/`) that prints what the rule found
  across many logs. Anything it writes to `data/` must hold no player names, and should de-duplicate by time so
  two characters' logs of one fight count once.

## 6. Plugin page

Everything for one mechanic lives in two files, `web/mechanics/<name>.js` and `web/mechanics/<name>.css`, plus a
`<script>` and a `<link>` line for them in `web/index.html`. Copy `mechanics/barrage.js`. It calls
`AugurTimeline.register({...})` with:

| Field | What it's for |
| --- | --- |
| `key`, `label`, `swatch` | Its one switch in the filter under the timeline, which turns all its marks on and off. |
| `legend` | Entries for the legend below the filter: `{ swatch, label }`. |
| `columns` | Count columns in the encounter list: `{ header, value(summary), className }`. |
| `items(summary)` | Its results, for `#select=<key>:<n>` links. |
| `times(summary)` | Epochs that widen the timeline when they fall outside ACT's start and end. |
| `topLanes(summary)`, `npcLanes(summary)`, `playerLanes(summary)` | Lanes it needs. Top lanes are shared lanes above the NPCs, for events with no speaker (event texts' "Narration"). Player lanes always include everyone in `summary.players`. |
| `draw(summary, api)` | Its marks. `api` gives `x(epoch)`, `topLane(name)`, `npcLane(name)`, `playerLane(name)`, `el()`, `clock()`, `isSelected()` and `selectable(node, item)`. |
| `panel: { title, render(body, summary) }` | Its folding panel on the right. `summary` is null while analysis runs. |
| `section: { title, render(body, summary, ctx) }` | Optional full-width list under the timeline. `ctx` gives `encounter`, `host` (running in the plugin), `start`, `select(item)`, which selects an item as a click on its mark does, and `refresh(gone)`, which redraws the timeline and sections after the mechanic changed what it shows (and closes `gone`'s detail if it's open). Rendered again when the encounters data changes. |
| `showDetail(view, item, fight)` | The box under the timeline when a mark is clicked: fill `view.title`, `view.summary`, `view.lines` (`AugurUtil.fillLines`). |
| `dataFlag` | Optional yes/no column on the Encounters data tab: `{ header, field, describe(name) }`. |

Lanes never change with the filter: NPC and player lanes stay when marks are switched off. Write log text with
`textContent` only (`AugurUtil.make` does). Keep a mechanic's colours as variables in its own CSS file.

## 7. Encounters data

Only if the mechanic is a per-NPC trait worth remembering: add a flag to `NpcRecord` in
`src/AugurACT.Core/Mechanics/EncounterStore.cs` and set it in `Update` from the casts, and give the mechanic a
`dataFlag` in its JS file. Flags only turn on, never off. Add a column for it to the encounter and boss/add tables on the
Encounters data tab. Don't put mechanic counts in the "updated" message; the store is mechanic-neutral.

If the user edits something per encounter (like event texts), see the extension points in
[Architecture](ARCHITECTURE.md#extension-points): store edits go in `StoreEditSet`, settings that change
analysis in `ScanOptions`, and page tabs or data sections in `AugurPage.register`.

## 8. ACT pitfalls already hit

| Problem | Fix |
| --- | --- |
| ACT keeps adding to `LogLines` after `OnCombatEnd`, most of all during an import; enumerating throws "Collection was modified" | Copy by index; re-analyze when the line count changes |
| `GetTypes()` runs before the assembly resolver, so a value-type field of a dependency type in `AugurACT.dll` breaks loading | Only reference-type fields in the plugin; check with a MetadataLoadContext field scan |
| Label and page updates from the analysis thread | Marshal through `BeginInvoke` |
| Errors are invisible in the page | `WriteExceptionLog` goes to `%APPDATA%\Advanced Combat Tracker\Advanced Combat Tracker.log`; read it first when something fails in ACT |

## 9. Checking the page without ACT

Build a harness: copy `web/` to a scratch folder, drop the CSP line, and load a fake `window.chrome.webview` that
answers the page's requests with data cut from the CLI's HTML output. Drive clicks from the URL hash and take
screenshots with headless Edge (`msedge.exe --headless=new --screenshot --virtual-time-budget=3000`).

- A screenshot after a programmatic scroll can come out blank; dump the DOM (`--dump-dom`) to check instead.
- Act on a `<dialog>` in its form's `submit` event. `close` fires later, after another dialog may have opened.

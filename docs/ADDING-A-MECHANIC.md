# Adding a mechanic

How to take a boss mechanic from "players know it exists" to "the plugin shows it per encounter and records
it per NPC". Barrage and bulwark was the first one done this way; each step names the barrage code to copy.

## 1. Find it in real logs

- **Ask the user for the shape of the mechanic and a known example**, including a known failure. Their
  examples (like the Oct 12 Peris K'dal group misses) save hours of searching, and failures are rare.
- **Learn the game rules the log can't show.** For barrage: zone tags tell raid from group, and a raid
  bulwark only covers the caster's group. Record these in [Mechanics](MECHANICS.md) and in memory.
- **Prefilter the archive first.** The logs are tens of GB. Grep each file down to the lines the mechanic
  could involve, keeping the `eq2log_<Character>` file names (the CLI reads the logger's name from them), and
  run everything else on the filtered copies. The full barrage run took 97 s on 12 GB; filtered it took 0.3 s.
  Keep the zone line (`] You have entered `) in the filter.
- **Write down the line sequence** for each content type, with exact text: warning emote, coloured text,
  player response, release, damage, cancel. Raid and group versions usually differ.

## 2. Decide the outcome from the effect, not the response

- **The logger only sees their own group's responses.** A raid barrage blocked by another group shows no
  bulwark at all. Judge success by what the mechanic did (who took damage) and keep the responses as evidence.
- **Check the rule against the counter-cases** before trusting it: successes with no visible response, and
  failures despite one. For barrage, 67 raid blocks had no bulwark in our group and 29 misses had one.
- **Check how often the "failure signal" happens anyway.** Flurries looked like a raid miss signal until the
  baseline showed them in 7 to 16% of all seconds.
- **No damage can mean success.** Some blocked barrages log nothing at all.
- **Keep "no outcome" as an outcome.** Bosses sometimes warn and never release; that's "No release", not a
  detection bug.
- Confirm odd cases with the user rather than inventing rules. They settled three open questions in one reply.

## 3. Detector in Core

Copy `src/AugurACT.Core/Mechanics/BarrageDetector.cs` and `BarrageCast.cs`:

- A static `MightMatter(rawLine)` cheap substring check. `EncounterSummary.Build` only fully parses lines that
  pass it, and the CLI prefilter uses it too.
- `Feed(Eq2LogLine)` per line in time order, `Complete()` at the end. Expire open casts by time windows
  (lead time, hit window, late-response window) rather than waiting for the end.
- Match the boss by its **exact NPC name prefix**. Names hold apostrophes and commas
  (`Mo'iana the Ravenous' Barrage`, `Evuntressa, Kryterion's Behest`), which the generic parser splits wrongly.
- Targets in emotes are upper case: compare without case. `YOU` is the logger; the logger's name comes from
  ACT (`charName` or the encounter's `CharName`) or the log file name.
- Keep the source lines on each cast (stripped of markup), so the page can show why a verdict was made.
- Add a `ContentKind` aware variant if raid and group differ.

## 4. Tests

Synthetic fixtures only; real logs hold other players' names and chat. Cover each outcome for each content
type, the logger as the target, missing warning lines, player abilities with similar names, and catalog
de-duplication. See `tests/AugurACT.Core.Tests/BarrageDetectorTests.cs`.

## 5. CLI and the boss catalog

Add a CLI subcommand like `barrage` (`src/AugurACT.Cli/Commands/BarrageCommand.cs`): per-boss table, `--catalog`
to add to `data/boss-mechanics.json` (no player names, safe to commit; casts de-duplicated by time so two
characters' logs of one fight count once), and `--html` for a standalone timeline page. That page holds player
names: never commit or share it (`.gitignore` covers `*timeline*.html`).

## 6. Plugin page

- Add the casts to `EncounterSummary` so they travel in the `analysis` message.
- Encounter list: count columns for the outcomes that matter.
- Timeline (`web/timeline.js`): a lane per NPC, colour per outcome, ticks for player responses.
- Right-hand column: one folding `.side-panel` per mechanic (`data-panel="<name>"`), same width open or
  closed. Panels stack and the column scrolls; don't make a mechanic its own layout.
- Write log text with `textContent` only.

## 7. Encounters data

Add a flag to `NpcRecord` in `src/AugurACT.Core/Mechanics/EncounterStore.cs` and set it in `Update` from the
casts. Flags only turn on, never off. Add a column for it to the encounter and boss/add tables on the
Encounters data tab. Don't put mechanic counts in the "updated" message; the store is mechanic-neutral.

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

# Architecture

## Layout

```
src/AugurACT.Core      netstandard2.0   Log parsing and analysis. No ACT or UI references.
src/AugurACT.Plugin    net48            ACT entry point. Builds AugurACT.dll. Must stay minimal (see below).
src/AugurACT.Ui        net48            WebView2 host control.
src/AugurACT.Ui/web                     The page shown in the plugin tab. The CLI inlines it into a standalone page.
src/AugurACT.Ui/web/mechanics           One .js and one .css per mechanic: its columns, marks, panel and legend.
src/AugurACT.Cli       net10.0          Runs Core over saved log files to tune rules. Runs on Linux/WSL.
data/boss-mechanics.json                Boss catalog built by the CLI. Holds no player names.
tests/AugurACT.Core.Tests  net10.0      xUnit tests for Core. Runs on Linux/WSL.
```

## Data flow

```
ACT ZoneList / OnCombatEnd -> EncounterBridge -> page: encounter list
page: ready, list, analyze, encounter-npcs, update-encounter -> EncounterBridge -> background thread -> Core
                                                                                    |
                         page <- PostWebMessageAsJson (UI thread) <- analysis, encounter-data
```

The page and plugin exchange small JSON messages, defined in `Core/Host/HostMessages.cs`:

| From page | Plugin answers with |
| --- | --- |
| `ready`, `list` | `encounters` (the whole list, newest first) and `encounter-data` |
| `analyze` (id) | `analysis` (the encounter's summary with its barrages and player deaths) |
| `encounter-npcs` (id) | `encounter-npcs`: the fight's enemy NPCs, most damage taken first |
| `update-encounter` (id, bosses) | `encounter-data` with the update's result |

A newly ended encounter arrives as `encounters` with `replace: false`. After a list, the plugin analyzes every
encounter in the background, newest first; requests from the page go ahead of that queue. Encounter ids are
built from the zone and encounter start times and ACT's `EncId`, and looked up again in `ZoneList` if needed.

**ACT keeps adding lines after `OnCombatEnd`**, most of all during an import. Enumerating `LogLines` then
throws "Collection was modified", so the plugin copies lines by index, and treats a cached summary as stale
when the encounter's line count has changed since.

## Mechanics

Each mechanic is a detector in Core and a pair of files in the page, joined by one list on the summary:

```
Core   IMechanicDetector (BarrageDetector, DeathDetector)  <- MechanicSet.Create, with a ScanContext and PlayerRoster
       EncounterSummary.Build runs them all over an encounter's lines -> summary.barrages, summary.deaths, summary.players
Plugin EncounterBridge sends the summary as "analysis"; CLI "timeline" cuts logs into fights (FightSplitter) and embeds it
Page   web/timeline.js draws lanes and the axis; web/mechanics/*.js register columns, marks, panels and legend
```

Adding one is described in [Adding a mechanic](ADDING-A-MECHANIC.md).

## Design rules

**Core stays free of ACT and WinForms.** All analysis must run and be tested from WSL against real logs,
without ACT or the game. This is what makes anecdotal rule tuning practical. The plugin only adapts ACT's
data and hosts the UI.

**Don't block ACT's parse thread.** Analysis and encounter store writes run on one background thread, and results
return to the UI thread through ACT's main form.

**Send JSON to the page, never HTML.** Log text includes player names and chat, so the page writes it with
`textContent`, and a Content Security Policy limits it to bundled files.

## Plugin loading

| Concern | Handling |
| --- | --- |
| ACT loads plugins from raw bytes, so .NET can't search the plugin's folder | `AssemblyResolve` handler looks there |
| Native `WebView2Loader.dll` isn't found by default | `SetLoaderDllFolderPath` selects the x64 or x86 copy |
| ACT's folder may not be writable | WebView2 data goes in `%LOCALAPPDATA%\AugurACT\WebView2` |
| `file://` restrictions on the page | Files are served from the virtual host `https://augur.act/` |

**Keep `AugurACT.dll` free of WebView2 code.** ACT calls `GetTypes()` on the plugin before `InitPlugin`, so
before the resolve handler exists. Any type there that forces a dependency to load makes the plugin fail with
"Could not load file or assembly". Not every reference does this. Reference-type fields are fine, but value-type
fields are not, and that includes the `TaskAwaiter<CoreWebView2Environment>` the compiler generates for an `await`.
WebView2 code therefore lives in `AugurACT.Ui`, which is first touched only after the handler is registered.

## Build

- `Advanced Combat Tracker.exe` is referenced at build time through `ActDir` but never copied or committed.
  Set `ActDir` in `Directory.Build.local.props`, which is gitignored.
- Setting `AugurDeployDir` in the same file copies the output there after each build.

## Known risks

- **.NET version:** ACT 3.8.5 declares .NET Framework 4.7 in its config, while this plugin targets 4.8. The
  CLR loads it as long as 4.8 is installed, which is standard on current Windows.
- **Test runtime:** tests run on .NET 10 while the plugin runs on .NET Framework. Regex behavior could differ
  at the edges, and Core can't use APIs beyond netstandard2.0 anyway.

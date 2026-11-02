# Plan

## Goal

Turn an EQ2 encounter log into an understanding of the fight:

1. **Key events:** what mattered in this fight, and when.
2. **Behavior patterns:** what the boss does, how often, and what warns of it.
3. **Key event logs:** the actual log lines behind each finding.
4. **Interactive timeline:** explore all of the above visually, inside ACT.
5. **Trigger regexes:** generate triggers for the events worth reacting to.

Parsing rules are expected to start anecdotal and be tuned against real fights, so rules must be cheap to
change and easy to re-run against old logs.

## Scope

- EverQuest II, English clients only. Other games are not planned, so no game abstraction layer.
- Runs inside ACT, on the log lines ACT already collects for an encounter.

## Phases

Each phase should be usable on its own and checked against real logs.

### Phase 0: Framework (done, pending the first load in ACT)

- [x] Git repo, solution, and build from WSL against the real ACT executable
- [x] `AugurACT.Core` with no ACT or UI dependency, plus unit tests
- [x] Plugin entry point, WebView2 tab, and a message path from ACT to the page
- [ ] Load in ACT and check the items under "Verify in ACT" below

### Phase 1: Log normalization

Turn raw lines into typed events: damage, heal, ward, kill, cure, interrupt, NPC speech and NPC emote.

- Strip `\aNPC`, `\aPC` and `\aITEM` link markup, keeping NPC instance IDs.
- Parse scaled numbers (`1.2B`, `7,438,675,968`).
- Add a small command-line tool that runs Core over a saved log file, so rules can be tuned on real logs
  without launching ACT or the game.
- Report on lines no rule recognizes; this list drives what to add next.

### Phase 2: Fight segmentation

Identify the boss and split logs into pulls, using NPC instance IDs, zone changes and activity gaps.

### Phase 3: Behavior analysis

- Per-ability statistics: use count, first use, interval between uses, targets, total damage.
- Telegraph pairing: an emote or speech line that reliably comes before an ability, and the lead time.
- Phase detection: changes in the ability mix or cadence across a fight.
- Compare pulls of the same boss, to separate fixed scripts from random behavior.
- Filter out player noise, such as class proc lines reported on the boss (see the log format notes).

### Phase 4: Interactive timeline

- Choose a library, with vis-timeline and D3 as the candidates. Bundle it with the plugin; don't use a CDN.
- One lane per ability or event type, with zoom, pan, and click-through to source log lines.
- Send aggregated data to the page. A long raid fight is far too many lines to render individually.

### Phase 5: Trigger generation

- Generate a regex from a chosen event, replacing variable parts (player names, numbers) with capture groups.
- **Test each regex against stored logs** and show hits and false positives before offering it.
- Export for ACT custom triggers and for Triggernometry, which is already installed here.

### Phase 6: Rule tuning

- Per-boss notes and overrides in an editable file: rename events, mark them key, or ignore them.
- Learn which rules should be global and which belong to a specific boss.

## Verify in ACT

These can't be checked without running inside ACT:

- [ ] The plugin loads and finds its dependencies from its own folder.
- [ ] A net48 plugin loads under ACT's declared .NET 4.7 runtime (see Architecture).
- [ ] WebView2 renders in the tab, in both `Advanced Combat Tracker.exe` and `ACTx86.exe` if the latter is used.
- [ ] `EncounterData.LogLines` is populated when `OnCombatEnd` fires. The table's "Lines parsed" column
      shows this: a total of 0 or a low parsed count means the assumption is wrong.
- [ ] Disabling and re-enabling the plugin works without restarting ACT.

## Open questions

- **Data source:** is ACT's in-memory encounter enough, or should analysis also read log files directly to
  reach older fights? ACT also keeps a history database worth examining.
- **Which encounter to analyze:** every encounter automatically, or only ones the user picks? Automatic
  analysis of trash fights may be wasted work.
- **Multi-mob fights:** ACT's encounter boundaries may not match a logical boss fight with adds.
- **Rule storage:** where tuned rules live, and whether they should be shareable.
- **License and hosting:** not chosen yet.

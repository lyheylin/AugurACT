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

**Not another damage graph.** ACT already shows who did what and how much. Every view here should answer
something ACT doesn't, such as what mattered in the fight and what the boss will do next. How to aggregate the
data follows from identifying what matters, so that design is deliberately left open for now.

## Scope

- EverQuest II, English clients only. Other games are not planned, so no game abstraction layer.
- Runs inside ACT, on the log lines ACT already collects for an encounter.

## Phases

Each phase should be usable on its own and checked against real logs.

### Phase 0: Framework (done)

- [x] Git repo, solution, and build from WSL against the real ACT executable
- [x] `AugurACT.Core` with no ACT or UI dependency, plus unit tests
- [x] Plugin entry point, WebView2 tab, and a message path from ACT to the page
- [x] Load in ACT and show parsed encounters in the tab (see "Verify in ACT" for what remains)

### Phase 1: Log normalization (in progress)

Turn raw lines into typed events: damage, heal, ward, kill, cure, interrupt, NPC speech and NPC emote.

- [x] Strip `\aNPC`, `\aPC` and `\aITEM` link markup, keeping NPC instance IDs.
- [x] Parse scaled numbers (`1.2B`, `7,438,675,968`).
- [x] Add a command-line tool that runs Core over a saved log file, so rules can be tuned on real logs without
      launching ACT or the game.
- [x] Report on lines no rule recognizes; this list drives what to add next.
- [ ] Review the unrecognized lines (17% of the first log) and decide which matter. The goal isn't 100%
      coverage, since most are player flavor text.
- [ ] Check the rules against other bosses and logs.

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

## How encounters get analyzed

The user selects an encounter to analyze; nothing runs automatically on every fight. Analysis builds on what
ACT already provides: its encounter boundaries and the data its parser has generated. It only re-reads raw log
lines for what ACT doesn't capture, such as NPC emotes and speech.

## Next steps

1. Identify specific logs and encounters to use as references, with their known mechanics.
2. List common patterns seen in those fights that rules can target.

## Verify in ACT

These can't be checked without running inside ACT:

- [x] The plugin loads and finds its dependencies from its own folder.
- [x] A net48 plugin loads under ACT's declared .NET 4.7 runtime (see Architecture).
- [x] WebView2 renders in the tab under `Advanced Combat Tracker.exe`.
- [ ] WebView2 renders in the tab under `ACTx86.exe`, if that is ever used.
- [x] `EncounterData.LogLines` is populated when `OnCombatEnd` fires. Every encounter in the first test parsed
      all of its lines, up to 837,508 lines in one encounter.
- [x] Disabling and re-enabling the plugin works without restarting ACT. The list starts empty again, because
      encounters are only captured as ACT finishes them.

## Open questions

- **Selecting encounters:** pick from ACT's own encounter list, from the plugin's list, or both. Listing
  ACT's encounters directly would also cover fights that finished before the plugin was enabled, which the
  plugin's list misses today. Check what ACT exposes.
- **Multi-mob fights:** ACT's encounter boundaries may not match a logical boss fight with adds. ACT already
  produces titles like `Illicus, Zon Zobboz Swarmer`, and trash pulls appear as encounters of their own.
- **Rule storage:** where tuned rules live, and whether they should be shareable.
- **License and hosting:** not chosen yet.

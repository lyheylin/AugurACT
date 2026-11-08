# AugurACT

An [Advanced Combat Tracker](https://advancedcombattracker.com/) plugin for EverQuest II. It analyzes
parsed encounters to find key events and boss behavior patterns, shows them on an interactive timeline,
and produces trigger regexes.

**Status:** early. The first mechanic, barrage and bulwark, is detected end to end. The plugin tab lists
ACT's encounters with their barrage counts; click one for its barrage timeline. "Update encounters data"
records a fight under its zone and main NPCs, with its adds and which of them release barrages. Each fight
also lists its emotes, NPC speech and narration, grouped by text with variables like `{player}`; these can be
added to a recorded encounter's event texts, or blacklisted, and given ACT triggers whose regexes match the
line as the game wrote it (copy as ACT's XML, or import straight into ACT). Other analysis is not built yet. See [the plan](docs/PLAN.md).

## Requirements

- .NET SDK 10
- ACT 3.8+ with the EQ2 English parsing plugin, for running the plugin (the tests don't need it)
- Windows with .NET Framework 4.8 and the WebView2 Runtime

## Build and test

```sh
cp Directory.Build.local.props.example Directory.Build.local.props   # then set ActDir
dotnet build
dotnet test
```

Builds and tests work from WSL or Windows. Tests cover `AugurACT.Core` only and need no ACT install.

## Analyze a saved log

```sh
dotnet run --project src/AugurACT.Cli -- summary "/path/to/eq2log_Character.txt"
```

This prints counts per event type, NPCs with speech or emotes, and the most common unrecognized lines.

```sh
dotnet run --project src/AugurACT.Cli -- timeline "/path/to/logs/" --since 2026-10-01 --html encounter-timeline.html
```

This cuts the logs into fights the way ACT does and writes the plugin's page as a file that opens from disk,
with every fight that had a mechanic in it. The page contains player names, so keep it out of the repository.

```sh
dotnet run --project src/AugurACT.Cli -- barrage "/path/to/logs/" --since 2026-09-03 --catalog data/boss-mechanics.json
dotnet run --project src/AugurACT.Cli -- deaths "/path/to/logs/" --since 2026-10-01
```

```sh
dotnet run --project src/AugurACT.Cli -- texts "/path/to/logs/" --data AugurACT.encounters.json
```

This lists each fight's event texts (emotes, NPC speech and narration), grouped with their variables, leaving
out what the encounters data file blacklists. `timeline` takes `--data` too. The output holds player names.

`barrage` and `deaths` check one mechanic's rules across many logs. `barrage` prints, per boss, how many barrages were blocked,
missed or cancelled, the warning time and how often it comes; `--catalog` adds new casts to the boss catalog,
skipping any already recorded. `deaths` lists player deaths per log and per character, with their most common
killers.

## Install in ACT

ACT needs a Windows path, so copy the build output somewhere Windows can reach. Setting `AugurDeployDir` in
`Directory.Build.local.props` does this on every build. Close ACT first, because a running plugin locks its
dependency DLLs.

1. Copy everything in `src/AugurACT.Plugin/bin/Debug/net48/` into its own folder, such as
   `%APPDATA%\Advanced Combat Tracker\Plugins\AugurACT\`.
2. In ACT, open **Plugins**, browse to `AugurACT.dll`, then choose **Add/Enable Plugin**.

## Documentation

- [Plan](docs/PLAN.md): goals, phases and open questions
- [Architecture](docs/ARCHITECTURE.md): project layout and design constraints
- [EQ2 log format](docs/EQ2-LOG-FORMAT.md): what real logs contain
- [Mechanics](docs/MECHANICS.md): boss mechanics AugurACT recognizes, and how
- [Adding a mechanic](docs/ADDING-A-MECHANIC.md): the steps and pitfalls, learned from barrage
- [Boss catalog](data/boss-mechanics.json): which bosses have which mechanics

## Logs and privacy

Real EQ2 logs contain other players' names and private chat, so don't commit them. `.gitignore` excludes
`eq2log_*.txt`; test fixtures should be synthetic.

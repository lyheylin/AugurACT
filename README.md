# AugurACT

An [Advanced Combat Tracker](https://advancedcombattracker.com/) plugin for EverQuest II. It analyzes
parsed encounters to find key events and boss behavior patterns, shows them on an interactive timeline,
and produces trigger regexes.

**Status:** early. The plugin tab lists ACT's encounters; click one for its timeline. A fight's emotes, NPC
speech and narration are grouped by text, with variables like `{player}`, and can be saved to the encounter,
blacklisted, or turned into ACT triggers. See [the plan](docs/PLAN.md) for what's done and what's next.

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

## Install in ACT

ACT needs a Windows path, so copy the build output somewhere Windows can reach. Setting `AugurDeployDir` in
`Directory.Build.local.props` does this on every build. Close ACT first, because a running plugin locks its
dependency DLLs.

1. Copy everything in `src/AugurACT.Plugin/bin/Debug/net48/` into its own folder, such as
   `%APPDATA%\Advanced Combat Tracker\Plugins\AugurACT\`.
2. In ACT, open **Plugins**, browse to `AugurACT.dll`, then choose **Add/Enable Plugin**.

## Use the plugin

- **Encounters:** the tab lists ACT's encounters. Select one to see its timeline.
- **Update encounters data:** records the fight under its zone and main NPCs, with its adds.
- **Event texts:** each fight's emotes, NPC speech and narration, grouped by text. Add them to the recorded
  encounter, or blacklist them.
- **Triggers:** give an event text an ACT trigger whose regex matches the line as the game wrote it. Copy it
  as ACT's XML, or import it straight into ACT.

## Analyze saved logs

The CLI runs the same analysis over saved log files, so rules can be tuned without launching ACT or the game.

```sh
dotnet run --project src/AugurACT.Cli -- timeline "/path/to/logs/" --since 2026-10-01 --html encounter-timeline.html
dotnet run --project src/AugurACT.Cli -- texts "/path/to/logs/" --data AugurACT.encounters.json
dotnet run --project src/AugurACT.Cli -- summary "/path/to/eq2log_Character.txt"
```

- `timeline` cuts the logs into fights the way ACT does and writes the plugin's page as a file that opens
  from disk.
- `texts` lists each fight's event texts with their variables.
- `summary` prints counts per event type, NPCs with speech or emotes, and the most common unrecognized lines.

`timeline` and `texts` take `--data` to leave out what the encounters data file blacklists. Their output holds
player names, so keep it out of the repository.

## Documentation

- [Plan](docs/PLAN.md): goals, phases and open questions
- [Architecture](docs/ARCHITECTURE.md): project layout and design constraints
- [EQ2 log format](docs/EQ2-LOG-FORMAT.md): what real logs contain
- [Mechanics](docs/MECHANICS.md): boss mechanics AugurACT recognizes, and how
- [Adding a mechanic](docs/ADDING-A-MECHANIC.md): the steps and pitfalls

## Logs and privacy

Real EQ2 logs contain other players' names and private chat, so don't commit them. `.gitignore` excludes
`eq2log_*.txt`; test fixtures should be synthetic.

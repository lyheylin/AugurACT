# AugurACT

An [Advanced Combat Tracker](https://advancedcombattracker.com/) plugin for EverQuest II. It analyzes
parsed encounters to find key events and boss behavior patterns, shows them on an interactive timeline,
and produces trigger regexes.

**Status:** early. The first mechanic, barrage and bulwark, is detected end to end, with a timeline in the
plugin tab and a boss catalog. Other analysis is not built yet. See [the plan](docs/PLAN.md).

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
dotnet run --project src/AugurACT.Cli -- barrage "/path/to/logs/" --since 2026-09-03 \
    --catalog data/boss-mechanics.json --html barrage-timeline.html
```

This finds every barrage in the logs and prints, per boss, how many were blocked, missed or cancelled, the
warning time and how often it comes. `--catalog` adds new casts to the boss catalog, skipping any already
recorded. `--html` writes a timeline page that opens from disk. The page contains player names, so keep it
out of the repository.

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
- [Boss catalog](data/boss-mechanics.json): which bosses have which mechanics

## Logs and privacy

Real EQ2 logs contain other players' names and private chat, so don't commit them. `.gitignore` excludes
`eq2log_*.txt`; test fixtures should be synthetic.

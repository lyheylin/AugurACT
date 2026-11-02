# Architecture

## Layout

```
src/AugurACT.Core      netstandard2.0   Log parsing and analysis. No ACT or UI references.
src/AugurACT.Plugin    net48            ACT entry point. Builds AugurACT.dll. Must stay minimal (see below).
src/AugurACT.Ui        net48            WebView2 host control.
src/AugurACT.Ui/web                     The page shown in the plugin tab.
src/AugurACT.Cli       net10.0          Runs Core over saved log files to tune rules. Runs on Linux/WSL.
tests/AugurACT.Core.Tests  net10.0      xUnit tests for Core. Runs on Linux/WSL.
```

## Data flow

```
EQ2 log -> ACT + EQ2 parser -> OnCombatEnd -> AugurPlugin -> Core (background thread)
                                                                  |
                                     web page <- PostWebMessageAsJson <- UI thread
```

## Design rules

**Core stays free of ACT and WinForms.** All analysis must run and be tested from WSL against real logs,
without ACT or the game. This is what makes anecdotal rule tuning practical. The plugin only adapts ACT's
data and hosts the UI.

**Don't block ACT's parse thread.** The handler copies what it needs, does the work in a background task,
and returns to the UI thread through ACT's main form.

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

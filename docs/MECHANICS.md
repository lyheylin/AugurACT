# Boss mechanics

What each recognized mechanic looks like in the log, and how AugurACT decides its outcome. The bosses known to
have each mechanic, with their stats, are in [data/boss-mechanics.json](../data/boss-mechanics.json).

## Barrage and bulwark

Many bosses warn that they're about to release a barrage. Fighters answer with a group bulwark, and a barrage
released while the bulwark is up is mitigated.

Findings come from 43 logs between Aug 30 and Nov 2 2026 (three characters, about 12 GB), covering 1,625 barrages
from 35 bosses. Detection is in `src/AugurACT.Core/Mechanics/BarrageDetector.cs`.

### Log lines

| Step | Raid (targeted) | Group (untargeted) |
| --- | --- | --- |
| Warning | `\aNPC <id> Boss:Boss\/a readies a barrage!` | same |
| Coloured warning | `\#FF00FFBoss prepares to unleash a barrage of attacks toward PLAYER!` | same, without `toward` |
| Bulwark | `Fighter applies a bulwark to the group.` | same |
| Release | `\aNPC <id> Boss:Boss\/a releases a barrage of attacks at PLAYER!` | same, without `at` |
| Damage | `Boss's Barrage hits Player for ...` | `Boss's Barrage: Unfathomable Foray hits Player for ...` |
| Cancel | `\#FA58ACBoss cancels the barrage, preferring to perform another task!` | same |

- **Warning time** is 10 to 11 seconds in raids and 5 to 6 seconds in groups.
- **The target** is upper-cased in both emotes. Matching it against damage lines must ignore case.
- **Either warning line can be missing.** One release had only the coloured warning, so both start a cast.
- **Bulwark expiry isn't useful.** `Fighter's bulwark fades.` comes 9 to 14 seconds after the apply in these
  logs, not 7.
- **Possessives vary**: `Mo'iana the Ravenous' Barrage`. Match on the releasing NPC's exact name, because
  the general parser splits names such as `Evuntressa, Kryterion's Behest` at the wrong apostrophe.
- **Not every barrage is this mechanic.** `The Arch Lich breaks from the portal to tear apart everyone with a
  barrage of attacks!` and its `Barrage: Nerve Splice` have no `releases` line, so they're ignored.

### Deciding the outcome

The outcome comes from barrage damage in the 6 seconds after the release, not from whether a bulwark was seen:

| Outcome | Raid | Group |
| --- | --- | --- |
| Blocked | `Barrage` hits only the named target, or no one | no `Barrage:` damage at all |
| Missed | `Barrage` also hits other players | any `Barrage:` damage, even "fails to inflict any damage" |
| Cancelled | cancel emote | cancel emote |
| No release | warning, then no release or cancel within 20 s | same |

A blocked barrage sometimes does no damage even to the target, so no damage at all counts as blocked. A boss
can also warn and then never release; that's a real outcome, shown as "No release", not a detection gap.

**In raids, only the target's group needs a bulwark.** A miss splashes 4 to 5 players, the rest of the
target's group, with about 10 to 15B focus damage each. A target in a group with no fighter can't be blocked;
that is still recorded as a miss, with the target named.

**Bulwarks are only logged for your own group**, so bulwark presses are kept as supporting evidence only:

- In groups, the two agree completely. All 170 releases with a bulwark in the previous 12 seconds were blocked.
  16 of the 24 without one were missed, and the other 8 had no damage for reasons the log doesn't show.
- In raids, 67 releases were blocked with no bulwark seen, because another group's fighter covered the target.
  29 were missed although our group had a bulwark up, because the target was in another group.

Damage on the target alone doesn't separate the outcomes. Bulwarked and unbulwarked hits on the target overlap
heavily, and the boss's normal flurries look much like a barrage.

### Settled questions

- **No release:** about 4% of warnings, 14 of them on Thet-ki-dua in Revelations of Consequence, have no
  release or cancel. The boss doesn't always release, so these are their own outcome.
- **No damage line:** Gawk, Gaze and Glare Oglth and Temper never produced a `Barrage` hit. A blocked
  barrage can do no damage, so these count as blocked.
- **Fighterless groups:** not split out. A miss names its target, which is enough.

## Player deaths

Every encounter counts each player's deaths, as ACT's Deaths column does, and marks them on the timeline.
Detection is in `src/AugurACT.Core/Mechanics/DeathDetector.cs`. Findings come from 15 logs in October and early
November 2026 (2,837 deaths).

| Line | Meaning |
| --- | --- |
| `Boss has killed Player.` | What ACT counts. NPCs, pets and adds dying use the same line. |
| `Boss has killed you.` | The logger died. Counted under the logger's name. |
| `Alas, Player has died from pain and suffering.` | A death with no killer named. ACT doesn't count these. |

- **Both lines usually come for one death**, in the same second. Lines for the same player within 5 seconds are
  one death; dying again after a rez takes longer.
- **"Pain and suffering" is sometimes the only line.** Instant-kill mechanics killed players through 19T of ward
  with only this line. So our count can be higher than ACT's; the Deaths panel says by how many.
- **Pets die from pain and suffering too** (`Alas, Player's thug ...`). Names with `'s ` are never players.
- **Some bosses have one-word names that look like players'**: Senzu, Mozal, Nazkun and Qworux kill players and
  get killed by them. Player names are one capitalised word, but that alone isn't enough.
- **Who is a player** comes from `PlayerRoster`, shared by all mechanics. In the plugin, ACT's ally list for the
  encounter decides (plus the logger). Without it (the CLI), a one-word name is a player when it was linked in
  group or raid chat (General and auction chat come from anyone in the zone), was in a heal between two
  different characters, or was killed by something that can't be a player (several words, lower case, or an
  NPC link). A one-word name killed by a known player is a named mob. "Pain and suffering" also kills NPC
  adds with one-word names, so it only counts for a known player.
- **Charmed players speak as NPCs**: `\aNPC 277422 Player:Player\/a says, "Your blood calls to me."` So an
  NPC link only rules a name out when nothing shows it's a player.
- **Named pets and mercenaries aren't players.** Summoner pets and mercenaries get random one-word names
  (Gantik, Gonober, Kabeker, Voser, Zaranab), take heals and wards, melee (`Gantik hits ...`) and die. They never
  use a named ability, while every player does, thousands of times a fight. So a player must also have a line
  like `Player's Ability hits ...` (or `Players' Ability` for names ending in s) in the encounter, with ACT's
  ally list too. On Oct 10 that left exactly 24 in every raid boss fight and 6 in every group fight.
- **A player who never used an ability isn't listed**, and their deaths aren't counted. That would take dying
  at the pull before casting anything.

Check the rule against saved logs with `AugurACT.Cli deaths <logs>`, which lists deaths per character with
their most common killers. It needs the full logs, not a prefiltered copy, because ability lines decide who's
a player.

## Event texts

The game tells players what's happening through emotes, NPC speech and narration. The chat channel isn't in
the log, so `EventTextDetector` tells them apart by shape:

| Kind | Shape |
| --- | --- |
| Emote | `\aNPC <id> Boss:Boss\/a readies a barrage!` |
| NPC say | `\aNPC <id> Boss:Boss\/a says, "..."` (also shouts, yells, whispers, tells) |
| Narration | A colour code outside any link, on a line that isn't player chat: `\#00FFFFThresinet targets Tesla for Flesh Decay!` |

Lines starting with an NPC link followed by `'s` are combat lines, not emotes. Item links carry their own colour
code (`Repaired: \aITEM ...:\#f0c0a0Ring\/a`), so links are removed before looking for one.

**Grouping.** Lines of one kind that differ only in their variable parts are one group, written as a template
(`EventTextTemplate`): `{player}`, `{class}`, `{archetype}` and `{number}`, with a literal brace doubled.

- **Player names are always variables.** They come from the encounter's player list (ACT's allies, or the
  roster in the CLI), matched as whole words in any case (`TROLLISHUS`), plus `YOU` in capitals.
- **Classes, archetypes and numbers** (`MAGE`, `20`, `1.2B`) become variables only when more than one value was
  seen in the encounter; otherwise they stay as text. The dialog lets the user switch any of them.
- **Each class or archetype in a list is its own variable**, and values never hold commas. Heroic Surge names
  one, two or three archetypes, so it makes a group per count (`{archetype}!`, `{archetype}, {archetype}!`, and
  `FIGHTER, PRIEST, SCOUT!` when only that one was seen), each blacklisted on its own.
- Charmed players speak as NPCs (`\aNPC 277422 Tesla:Tesla\/a says, "Your blood calls to me."`); those go in the
  player's own lane, with the name as `{player}`.

**Not filtered out on purpose:** player-triggered narration (Heroic Surge), ability-ready notices ("Time Warp is
now ready for use!") and system text look exactly like encounter narration. The user blacklists them. Nothing is
blacklisted by default.

**Templates become regexes** (`EventTextTemplate.ToRegex`): literal text escaped, each variable a capture group,
the whole line anchored, case ignored. **The variables are defined only in `EventTextTemplate`.** The page gets
their names, patterns and the "could be a variable" pattern from Core (`EventTextConfig`, in the `config`
message, or `report.config` in the CLI's page) and builds the same regexes from them, so patterns there must be
valid in both .NET and JavaScript (inline `(?i:...)` groups are fine in both). The
blacklist uses them now; trigger generation is meant to use them later.

**Timers.** A group that repeats on a steady timer shows how often: "every 3:40 ± 13 s" (`EventTextTimer`).

1. Lines of a group less than `SameEventSeconds` (2 s) apart are one event, like an AoE text logged per player.
2. With at least `MinEvents` (4) events, take the intervals between them.
3. Steadiness is their **coefficient of variation**, sample stdev ÷ mean. It has no unit, so a 20 s timer and a
   4 min one are judged alike. The timer is steady when it's at most `SteadyMaxVariation`, **0.15 for now**.
4. The interval shown is the mean; the ± is the largest distance of any interval from it, so it covers every
   interval seen.

How 0.15 was picked, from three logs (Olvorlec Nov 7, Akatsukifish Oct 31 and Nov 7), 101 groups with 4+ events:

| Variation | Groups | Examples |
| --- | --- | --- |
| under 0.05 | 35 | The Arm of Lucan's four mechanics, 0.034-0.039 (every ~220 s, ± 13-16 s) |
| 0.05 to 0.15 | 11 | Void Pox cures every ~20 s (0.08-0.14); "unable to synergize" every 4 s |
| 0.15 to 0.40 | 17 | Void Pox in longer pulls (0.22-0.29), Poxlord's "commits {player} to memory" (0.19-0.27) |
| 0.40 and over | 38 | Heroic Surge (0.86), Chaotic Influence (0.58) |

Still to tune: a single missed line or a phase where the boss stops doubles one interval and fails the test;
and short bursts with 1-second timestamps (lines 3-4 s apart) can pass as timers. Check
`AugurACT.Cli texts`, which prints every group's variation, before changing the value.

**Remove** takes a group off one fight's list and timeline while the page is open, without blacklisting it.
"Show again" under the list brings them back.

`AugurACT.Cli texts <logs> [--data encounters.json]` prints each fight's groups, with the values seen for any
variable class or number, and each repeating group's interval and variation (`STEADY` when it passes).

## Encounters data in the plugin

Separate from the catalog above, the plugin keeps the user's own record of encounters in
`%APPDATA%\Advanced Combat Tracker\Config\AugurACT.encounters.json`. It is written only when the user clicks
"Update encounters data" on an ACT fight and confirms its main NPCs.

- **An encounter is its zone plus its set of main NPCs** (bosses). Several can be chosen for bosses fought
  together. Recording another fight with the same zone and bosses adds to that encounter.
- **Every other NPC in the fight is an add**, listed under the encounter.
- **Each NPC carries its own mechanic flags.** Today that is only `releasesBarrage`: yes once any recorded fight
  showed that NPC releasing a barrage (blocked or missed). A later fight without a release never turns it back
  to no, and a cancelled or unreleased warning doesn't count. More mechanics will add their own flags.
- **Each encounter has a list of event texts** (`eventTexts`: kind, template, date added, and optionally the
  user's `description` and a `timer`). Texts are added from a fight's list under the timeline ("Add +", which
  picks the recorded encounter in the fight's zone whose bosses appear in it) or by hand on the Encounters data
  tab, and edited or removed there. Adding a text the encounter already lists updates that entry; its button in
  the fight's list reads "Update".
- **Timers in the encounters data** (`timer`: mean, ±, intervals, fights). When the text is on a steady timer in
  the fight, "Attach timer" (or "Update timer", if one is recorded) is ticked and filled in. Updating combines
  the fight with the recorded timer: the mean is weighted by intervals, the ± widens to cover both, and each
  fight (by start time) is counted once. Unticked, a recorded timer stays as it was. A timer can also be typed
  (seconds or m:ss); typed values count as typed by hand (no intervals). Editing an entry and unticking the
  timer removes it.
- **The blacklist** (`eventTextBlacklist`, in the same file) holds templates never to list. Blacklisting a text
  also removes it from every encounter's list. Summaries leave blacklisted lines out, so a change to the
  blacklist makes the plugin analyze every encounter again. Manage it on the Blacklist tab.

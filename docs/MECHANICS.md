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
| Blocked | `Barrage` hits only the named target | no `Barrage:` damage at all |
| Missed | `Barrage` also hits other players | any `Barrage:` damage, even "fails to inflict any damage" |
| Cancelled | cancel emote | cancel emote |
| Unresolved | warning, then no release or cancel within 20 s | same |

**In raids, only the target's group needs a bulwark.** A miss splashes 4 to 5 players, the rest of the
target's group, with about 10 to 15B focus damage each. A target in a group with no fighter can't be blocked.

**Bulwarks are only logged for your own group**, so bulwark presses are kept as supporting evidence only:

- In groups, the two agree completely. All 170 releases with a bulwark in the previous 12 seconds were blocked.
  16 of the 24 without one were missed, and the other 8 had no damage for reasons the log doesn't show.
- In raids, 67 releases were blocked with no bulwark seen, because another group's fighter covered the target.
  29 were missed although our group had a bulwark up, because the target was in another group.

Damage on the target alone doesn't separate the outcomes. Bulwarked and unbulwarked hits on the target overlap
heavily, and the boss's normal flurries look much like a barrage.

### Open questions

- **Unresolved barrages:** about 4% of warnings, 14 of them on Thet-ki-dua in Revelations of Consequence, have
  no release or cancel while the boss keeps fighting. Nothing in the log says why.
- **Bosses with no damage line:** Gawk, Gaze and Glare Oglth and Temper never produced a `Barrage` hit, even on
  the target. "Blocked" for them only means no splash was seen. A miss there may look different.
- **Group composition:** the log doesn't say who is in which raid group. Knowing it would let a miss be split
  into "no fighter in that group" and "late or forgotten bulwark".

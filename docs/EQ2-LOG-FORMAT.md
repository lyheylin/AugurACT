# EQ2 log format

Observations from real logs, mainly one raid log of about 4.7 million lines and 560 MB, including six pulls
of the boss Thet-ki-dua. **These findings come from one boss on one night.** Treat them as starting points to
check against other fights. Player names below are replaced with placeholders.

## Line structure

```
(1791593375)[Sat Oct 10 08:49:35 2026] Thet-ki-dua's Surging Pulse hits Player for 1.2B magic damage.
 ^ Unix epoch ^ local time, 24 chars   ^ body
```

- Timestamps have **one-second resolution**, so order within a second is only file order.
- The day is space-padded: `[Mon Nov  2 04:02:19 2026]`.
- Some lines have no timestamp, such as multi-line guild messages of the day. Skip them.
- Files are rotated with a date suffix: `eq2log_<Character>.2026.10.10.txt`.

## Link markup

Names in speech and emotes carry link markup. Damage lines usually use plain text.

```
\aNPC 4773594 Thet-ki-dua:Thet-ki-dua\/a says, "..."
\aPC -1 Player:Player\/a tells General (3), "..."
\aITEM 1160125644 1640598029:Item Name\/a
```

## Line types

The ACT parser's regexes (`ACT_English_Parser.cs`, around line 335) are the reference for combat lines.

| Type | Example |
| --- | --- |
| Damage | `Boss's Ability hits Player for 1.2B magic damage.` |
| Critical | `Player's Ability hits Boss for a Fabled critical of 420.3B poison damage.` |
| Multi/AoE | `a mob aoe attacks Player for 123 slashing damage.` |
| Heal | `Player's Ability heals Player for 1.4B hit points.` |
| Ward | `Player's Ability absorbs 1.2B points of damage from being done to Player. (N points remaining)` |
| Damage reduction | `Player reduces the damage from Boss to Player by 476528345.` |
| Hate | `Player's Ability reduces THEIR hate with Boss for a critical of 7,438,675,968 threat.` |
| Cure | `Player's Ability relieves Debuff IV from Player.` |
| Power drain | `Boss's Ability crushes Player draining 123 points of power.` |
| Kill | `Boss has killed Player.` (also `has killed you.`) |
| Death, no killer | `Alas, Player has died from pain and suffering.` |
| Interrupt | `Player was interrupted!` |
| Casting | `Player begins to gather strands of arcane energy.` |
| Zone | `You have entered Zone Name.` |

**The first person changes the wording:** `YOUR Ability`, `crushes YOU`. Rules must handle both forms.

Numbers appear scaled (`1.2B`) and in full (`7,438,675,968`, `476528345`), sometimes on the same line.

## Boss behavior signals

These lines are rare, and they're the most valuable for this plugin. In the sampled raid, the boss produced
only 423 linked lines, among millions of others.

### Telegraphs

```
\aNPC <id> Thet-ki-dua:Thet-ki-dua\/a readies a barrage!
\aNPC <id> Thet-ki-dua:Thet-ki-dua\/a releases a barrage of attacks at PLAYER!
```

130 "readies" lines and 121 "releases" lines. Of the 121 pairs, 120 had a gap of 10-11 seconds and one had
9 seconds. The target's name is **upper-cased** in this emote, which matters for trigger regexes.

This is the barrage and bulwark mechanic, which [Mechanics](MECHANICS.md) covers across 35 bosses.

Some emotes carry a colour code instead of an NPC link: `\#FF00FFBoss prepares to unleash a barrage of attacks!`.

### Other emotes and speech

```
\aNPC <id> Thet-ki-dua:Thet-ki-dua\/a attempts to avoid Player for the next 20 seconds!
\aNPC <id> Thet-ki-dua:Thet-ki-dua\/a says, "Quiwot Pillk Verathwa!"
```

### NPC instance IDs may mark pulls

The ID in `\aNPC <id>` changed between pulls of the same boss. In the sample, six IDs covered six separate
time windows, with no overlap between them:

| Window length | Linked lines |
| --- | --- |
| 1863 s | 104 |
| 70 s | 8 |
| 1814 s | 112 |
| 1693 s | 89 |
| 191 s | 16 |
| 1863 s | 94 |

Still to check: whether this holds for other bosses, and how it behaves when a boss has several mobs. IDs
only appear on linked lines, so they are sparse. A boss with no speech or emotes may never show one.

### Kills aren't a reliable end marker

`has killed Thet-ki-dua` appeared **zero** times across all six pulls, including any that may have been wins.
Don't rely on a kill line to detect that a boss fight ended successfully.

## Noise

Boss-named lines are often not boss actions:

- Player effects reported on the boss, at high volume: `An augmentation song affects Boss.` (about 101,000),
  `Boss is struck with a holy weapon!` (about 46,000), `Boss takes reprisal damage!` and
  `Boss's anger towards the Thief subsides.`
- Abilities in the boss's name that may be debuff ticks or reflects rather than casts. `Surging Pulse` has
  about 370,000 lines, and each cast hits many players. Group lines by timestamp to count casts.
- Ability tiers look like separate abilities: `Channeled Fury I` to `IV`. These may be one mechanic, so
  decide whether to merge them per boss.

Most lines the parser doesn't recognize are this kind of flavor text from player abilities, plus lines like
`Player's Ability regenerates N points of absorption.` In the sampled log, 17% of lines fell into this group.
They haven't been reviewed for boss mechanics yet, and some mechanics may only appear here without a link.

Pets include their owner in the name: `Player's puppet's Foretold Barrage multi attacks Boss for ...`.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AugurACT.Core.Encounters;
using AugurACT.Core.Eq2;

namespace AugurACT.Core.Mechanics
{
    // Finds player deaths. Feed lines in log order, then call Complete().
    //
    //   Boss has killed Player.                                   (what ACT counts as a death)
    //   Boss has killed you.                                      (the logger)
    //   Alas, Player has died from pain and suffering.            (no killer named; ACT doesn't count these)
    //
    // Both lines often come for one death, in the same second; they're merged. Kill lines also report NPCs and
    // pets dying, so who counts is decided at the end, with the PlayerRoster. See docs/MECHANICS.md.
    public sealed class DeathDetector : IMechanicDetector
    {
        // Lines for one player this close together are one death; a rez and a second death take longer.
        public const int SameDeathSeconds = 5;

        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private static readonly Regex Killed = new Regex(@"^(?<killer>.+?) (?:has|have) killed (?<victim>.+)\.$", Options);
        private static readonly Regex Pain = new Regex(@"^Alas, (?<victim>.+?) has died from pain and suffering\.$", Options);

        private readonly PlayerRoster roster;
        private readonly List<Candidate> candidates = new List<Candidate>();

        // The roster is shared with the encounter and fed by it; this detector only reads it, and adds the
        // victims of obvious NPCs.
        public DeathDetector(ScanContext context)
        {
            roster = context.Roster;
        }

        public bool MightMatter(string rawLine) =>
            rawLine.IndexOf(" killed ", StringComparison.Ordinal) >= 0
            || rawLine.IndexOf("pain and suffering", StringComparison.Ordinal) >= 0;

        public void Feed(Eq2LogLine line)
        {
            var body = line.Body;
            if (body.IndexOf(" killed ", StringComparison.Ordinal) >= 0)
            {
                var text = LogMarkup.Strip(body);
                var match = Killed.Match(text);
                if (match.Success)
                {
                    candidates.Add(new Candidate(
                        line.EpochSeconds, roster.Normalize(match.Groups["victim"].Value), roster.Normalize(match.Groups["killer"].Value), text));
                }

                return;
            }

            if (body.StartsWith("Alas, ", StringComparison.Ordinal))
            {
                var text = LogMarkup.Strip(body);
                var match = Pain.Match(text);
                if (match.Success)
                {
                    candidates.Add(new Candidate(line.EpochSeconds, roster.Normalize(match.Groups["victim"].Value), null, text));
                }
            }
        }

        public int Complete(EncounterSummary summary)
        {
            var deaths = Complete();
            summary.Deaths.AddRange(deaths);
            return deaths.Count;
        }

        public IReadOnlyList<PlayerDeath> Complete()
        {
            // Whoever an obvious NPC killed is a player, which identifies the one-word bosses' victims too.
            foreach (var c in candidates)
            {
                if (c.Killer != null && !CouldBePlayer(c.Killer) && !IsPet(c.Killer))
                {
                    roster.AddPlayer(c.Victim);
                }
            }

            var deaths = new List<PlayerDeath>();
            var last = new Dictionary<string, PlayerDeath>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in candidates)
            {
                if (!IsPlayer(c.Victim, c.Killer))
                {
                    continue;
                }

                var line = new SourceLine { At = c.At, Text = c.Text };
                if (last.TryGetValue(c.Victim, out var previous) && c.At - previous.At <= SameDeathSeconds)
                {
                    previous.Killer ??= c.Killer;
                    previous.Lines.Add(line);
                    continue;
                }

                var death = new PlayerDeath { Name = c.Victim, At = c.At, Killer = c.Killer, Lines = { line } };
                deaths.Add(death);
                last[c.Victim] = death;
            }

            deaths.Sort((a, b) => a.At.CompareTo(b.At));

            // A one-word name killed by an unknown one-word killer was taken for a player; list it as one.
            foreach (var death in deaths)
            {
                roster.AddPlayer(death.Name);
            }

            return deaths;
        }

        private bool IsPlayer(string victim, string? killer)
        {
            if (roster.IsSelf(victim))
            {
                return true;
            }

            if (!PlayerRoster.IsPlayerShaped(victim))
            {
                return false;
            }

            // Named pets and mercenaries die too; they never use a named ability.
            if (!roster.UsedAbility(victim))
            {
                return false;
            }

            if (roster.HasAllyList)
            {
                return roster.IsKnownPlayer(victim);
            }

            if (roster.IsKnownPlayer(victim))
            {
                return true;
            }

            if (roster.IsLinkedNpc(victim))
            {
                return false;
            }

            // Unknown one-word names: a named mob unless the killer could be an NPC. "Pain and suffering" also
            // kills NPCs, so with no killer the name has to be known.
            return killer != null && !roster.IsSelf(killer) && !IsPet(killer) && !roster.IsKnownPlayer(killer);
        }

        private bool CouldBePlayer(string name) =>
            roster.IsKnownPlayer(name) || (PlayerRoster.IsPlayerShaped(name) && !roster.IsLinkedNpc(name));

        private static bool IsPet(string name) => name.IndexOf("'s ", StringComparison.Ordinal) >= 0;

        private sealed class Candidate
        {
            public Candidate(long at, string victim, string? killer, string text)
            {
                At = at;
                Victim = victim;
                Killer = killer;
                Text = text;
            }

            public long At { get; }

            public string Victim { get; }

            public string? Killer { get; }

            public string Text { get; }
        }
    }
}

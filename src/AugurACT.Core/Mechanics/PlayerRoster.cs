using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AugurACT.Core.Eq2;

namespace AugurACT.Core.Mechanics
{
    // Who the players in an encounter are. In the plugin, ACT's ally list says who's on our side. Without it (the
    // CLI), the log has to show it: a player link in group or raid chat (General and auction chat come from anyone
    // in the zone), a heal between two different characters, or a mechanic that can only happen to a player
    // (DeathDetector adds those it's sure of).
    //
    // Either way, a player must also have used a named ability ("Player's Ability hits ...", or "Players' ..."
    // for names ending in s). Pets and mercenaries with their own names (Gantik, Voser) only melee
    // ("Gantik hits ...") and take heals, a handful of times a fight, while players cast thousands of abilities.
    //
    // Player names are one capitalised word, but some bosses have names like that too (Senzu, Mozal), so the
    // shape alone isn't enough. An NPC link counts against a name only when nothing shows it's a player:
    // charmed players speak as NPCs ("\aNPC 277422 Player:Player\/a says, "Your blood calls to me."").
    public sealed class PlayerRoster
    {
        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private static readonly Regex Link = new Regex(@"\\a(?<kind>NPC|PC) -?\d+ (?<name>[^:\\]+):", Options);
        private static readonly Regex PlayerShapedName = new Regex(@"^[A-Z][a-z]+$", Options);

        // "Healer's Ability heals Target for", "YOUR Ability heals Target for", "Healer's Ability heals YOU for".
        private static readonly Regex Heal = new Regex(
            @"^(?:(?<healer>[A-Z][a-z]+)'s?|YOUR) .+? (?:critically )?heals (?<target>[A-Z][a-z]+|YOU) for ", Options);

        // What follows "Name's Ability" when a character uses one.
        private static readonly string[] AbilityVerbs =
        {
            " hits ", " heals ", " absorbs ", " multi attacks ", " flurries ", " double attacks ", " aoe attacks ",
            " critically ", " refreshes ", " increases ", " reduces ", " relieves ", " dispels ", " bashes ",
        };

        private readonly string? selfName;
        private readonly HashSet<string>? allies;
        private readonly HashSet<string> linkedNpcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> abilityUsers = new HashSet<string>(StringComparer.Ordinal);

        // allies is ACT's ally list; null or empty means work it out from the log.
        public PlayerRoster(string? selfName = null, IEnumerable<string>? allies = null)
        {
            this.selfName = string.IsNullOrEmpty(selfName) ? null : selfName;
            if (allies != null)
            {
                var set = new HashSet<string>(allies.Where(IsPlayerShaped), StringComparer.OrdinalIgnoreCase);
                this.allies = set.Count > 0 ? set : null;
            }
        }

        public bool HasAllyList => allies != null;

        public static bool IsPlayerShaped(string name) => PlayerShapedName.IsMatch(name);

        // Called with every raw line, parsed or not. Cheap: it reads the name a line starts with, and only looks
        // further the first time that name shows up with an ability.
        public void Observe(string rawLine)
        {
            // "(1791593375)[Sat Oct 10 08:49:35 2026] Body": the body starts at 39.
            const int body = 39;
            if (rawLine.Length < body + 8 || rawLine[body - 1] != ' ' || rawLine[body] < 'A' || rawLine[body] > 'Z')
            {
                return;
            }

            var end = body + 1;
            while (end < rawLine.Length && rawLine[end] >= 'a' && rawLine[end] <= 'z')
            {
                end++;
            }

            // "Name's Ability" or "Names' Ability": an apostrophe, maybe an s, a space, then a capital.
            var next = end;
            if (next >= rawLine.Length || rawLine[next] != '\'')
            {
                return;
            }

            next++;
            if (next < rawLine.Length && rawLine[next] == 's')
            {
                next++;
            }

            if (next + 1 >= rawLine.Length || rawLine[next] != ' ' || rawLine[next + 1] < 'A' || rawLine[next + 1] > 'Z')
            {
                return;
            }

            var name = rawLine.Substring(body, end - body);
            if (abilityUsers.Contains(name))
            {
                return;
            }

            foreach (var verb in AbilityVerbs)
            {
                if (rawLine.IndexOf(verb, next, StringComparison.Ordinal) >= 0)
                {
                    abilityUsers.Add(name);
                    return;
                }
            }
        }

        public bool UsedAbility(string name) => IsSelf(name) || abilityUsers.Contains(name);

        public bool MightMatter(string rawLine) =>
            rawLine.IndexOf(@"\aNPC ", StringComparison.Ordinal) >= 0
            || rawLine.IndexOf(@"\aPC ", StringComparison.Ordinal) >= 0
            || (allies == null && rawLine.IndexOf(" heals ", StringComparison.Ordinal) >= 0);

        public void Feed(Eq2LogLine line)
        {
            var body = line.Body;
            if (body.IndexOf(@"\a", StringComparison.Ordinal) >= 0)
            {
                var fellow = body.IndexOf(" says to the raid party", StringComparison.Ordinal) >= 0
                    || body.IndexOf(" says to the group", StringComparison.Ordinal) >= 0;
                foreach (Match link in Link.Matches(body))
                {
                    var name = link.Groups["name"].Value;
                    if (link.Groups["kind"].Value == "NPC")
                    {
                        linkedNpcs.Add(name);
                    }
                    else if (fellow)
                    {
                        AddPlayer(name);
                    }
                }

                return;
            }

            if (allies == null && body.IndexOf(" heals ", StringComparison.Ordinal) >= 0)
            {
                var match = Heal.Match(body);
                if (match.Success)
                {
                    var healer = match.Groups["healer"].Success ? match.Groups["healer"].Value : "YOU";
                    var target = match.Groups["target"].Value;
                    if (!string.Equals(healer, target, StringComparison.OrdinalIgnoreCase))
                    {
                        AddPlayer(healer);
                        AddPlayer(target);
                    }
                }
            }
        }

        // Log evidence that a name is a player. Ignored when ACT's ally list decides.
        public void AddPlayer(string name)
        {
            if (allies == null && !IsSelf(name) && IsPlayerShaped(name) && name != "Unknown")
            {
                seen.Add(name);
            }
        }

        public bool IsSelf(string name) =>
            name == "YOU" || (selfName != null && string.Equals(name, selfName, StringComparison.OrdinalIgnoreCase));

        public bool IsLinkedNpc(string name) => linkedNpcs.Contains(name);

        public bool IsKnownPlayer(string name) =>
            IsSelf(name) || ((allies != null ? allies.Contains(name) : seen.Contains(name)) && abilityUsers.Contains(name));

        // Kill lines and emotes say "you" for the logger.
        public string Normalize(string name) =>
            string.Equals(name, "you", StringComparison.OrdinalIgnoreCase) ? selfName ?? "YOU" : name;

        // Everyone known to have been in the encounter, the logger included, in name order.
        public List<string> Players()
        {
            var names = (allies ?? seen).Where(abilityUsers.Contains).ToList();
            var self = selfName ?? "YOU";
            if (!names.Contains(self, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(self);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}

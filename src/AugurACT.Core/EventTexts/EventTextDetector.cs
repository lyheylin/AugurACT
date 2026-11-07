using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AugurACT.Core.Encounters;
using AugurACT.Core.Eq2;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.EventTexts
{
    // Collects the texts the game uses to tell what's happening in a fight, and groups those that differ only
    // in their variable parts. The log drops the chat channel, so the kind comes from the line's shape:
    //
    //   \aNPC 4773594 Boss:Boss\/a readies a barrage!               emote
    //   \aNPC 4773594 Boss:Boss\/a says, "Kneel!"                   say
    //   \#FF00FFThresinet targets Tesla for Flesh Decay!            narration (a colour code, no NPC link)
    //
    // Player-triggered narration (Heroic Surge) and system text look the same; the user blacklists those.
    // Variables: player names always; a class, archetype or number only when it varies within the encounter.
    public sealed class EventTextDetector : IMechanicDetector
    {
        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private static readonly Regex Color = new Regex(@"\\#[0-9A-Fa-f]{6}", Options);
        private static readonly Regex AnyLink = new Regex(@"\\a[A-Z]+ -?\d+ .*?\\/a", Options);
        private static readonly Regex NpcSays = new Regex(@"^(?:says|shouts|yells|whispers|tells [^,]+),", Options);

        private readonly ScanContext context;
        private readonly List<Line> lines = new List<Line>();

        public EventTextDetector(ScanContext context)
        {
            this.context = context;
        }

        public bool MightMatter(string rawLine) =>
            rawLine.IndexOf(@"\aNPC ", StringComparison.Ordinal) >= 0
            || rawLine.IndexOf(@"\#", StringComparison.Ordinal) >= 0;

        public void Feed(Eq2LogLine line)
        {
            var body = line.Body;
            string kind;
            string? source = null;
            string text;
            if (LogMarkup.TryParseLeadingNpc(body, out _, out var name, out var rest))
            {
                // "\aNPC ... Boss\/a's Ability hits ..." is combat, not an emote.
                if (rest.StartsWith("'", StringComparison.Ordinal) || rest.Length == 0)
                {
                    return;
                }

                kind = NpcSays.IsMatch(rest) ? EventTextKind.Say : EventTextKind.Emote;
                source = name;
                text = name + " " + rest;
            }
            else if (IsNarration(body))
            {
                kind = EventTextKind.Narration;
                text = LogMarkup.Strip(body);
            }
            else
            {
                return;
            }

            text = text.Trim();
            if (text.Length == 0 || IsBlacklisted(text))
            {
                return;
            }

            lines.Add(new Line(line.EpochSeconds, kind, source, text));
        }

        public int Complete(EncounterSummary summary)
        {
            summary.EventTexts.AddRange(Complete());

            // Almost every fight has some; they don't make a fight worth listing on their own (CLI timeline).
            return 0;
        }

        public IReadOnlyList<EventTextGroup> Complete()
        {
            var tokens = Tokenizer(context.Roster.Players());
            var groups = new Dictionary<string, Builder>(StringComparer.Ordinal);
            var order = new List<Builder>();
            foreach (var line in lines)
            {
                var split = SplitLine(tokens, line.Text);
                var key = line.Kind + "\n" + split.Key;
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new Builder(line.Kind, split.Parts);
                    groups[key] = group;
                    order.Add(group);
                }

                group.Add(line, split.Values, context.Roster);
            }

            return order.Select(g => g.Build()).ToList();
        }

        private bool IsBlacklisted(string text)
        {
            foreach (var rule in context.Options.EventTextBlacklist)
            {
                if (rule.IsMatch(text))
                {
                    return true;
                }
            }

            return false;
        }

        // A colour code outside any link (item links carry their own colour), on a line that isn't player chat.
        private static bool IsNarration(string body)
        {
            if (body.IndexOf(@"\#", StringComparison.Ordinal) < 0 || body.StartsWith(@"\aPC ", StringComparison.Ordinal))
            {
                return false;
            }

            return Color.IsMatch(AnyLink.Replace(body, ""));
        }

        // Finds the parts of a line that could be variables. Player names match whole words in any case
        // ("TROLLISHUS"); "YOU", only in capitals, is the logger.
        private static Regex Tokenizer(IEnumerable<string> players)
        {
            var names = players
                .Where(p => p != "YOU" && PlayerRoster.IsPlayerShaped(p))
                .OrderByDescending(p => p.Length)
                .Select(Regex.Escape)
                .ToList();
            var player = names.Count > 0 ? "(?-i:YOU)|" + string.Join("|", names) : "(?-i:YOU)";
            var pattern = "(?<![A-Za-z0-9])(?:"
                + $"(?<{EventTextTemplate.Player}>{player})"
                + $"|(?<{EventTextTemplate.Class}>{EventTextTemplate.VariablePattern(EventTextTemplate.Class)})"
                + $"|(?<{EventTextTemplate.Archetype}>{EventTextTemplate.VariablePattern(EventTextTemplate.Archetype)})"
                + $"|(?-i:(?<{EventTextTemplate.Number}>{EventTextTemplate.NumberPattern}))"
                + ")(?![A-Za-z0-9])";
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static Split SplitLine(Regex tokens, string text)
        {
            var parts = new List<EventTextPart>();
            var values = new List<string>();
            var key = new StringBuilder();
            var at = 0;
            foreach (Match match in tokens.Matches(text))
            {
                var slot = EventTextTemplate.SlotKinds.First(k => match.Groups[k].Success);
                if (match.Index > at)
                {
                    var literal = text.Substring(at, match.Index - at);
                    parts.Add(new EventTextPart { Text = literal });
                    key.Append(EventTextTemplate.Escape(literal));
                }

                parts.Add(new EventTextPart { Slot = slot });
                values.Add(match.Value);
                key.Append(EventTextTemplate.Placeholder(slot));
                at = match.Index + match.Length;
            }

            if (at < text.Length)
            {
                var literal = text.Substring(at);
                parts.Add(new EventTextPart { Text = literal });
                key.Append(EventTextTemplate.Escape(literal));
            }

            return new Split(key.ToString(), parts, values);
        }

        private sealed class Builder
        {
            private readonly string kind;
            private readonly List<EventTextPart> parts;
            private readonly List<EventTextOccurrence> occurrences = new List<EventTextOccurrence>();

            public Builder(string kind, List<EventTextPart> parts)
            {
                this.kind = kind;
                this.parts = parts;
            }

            public void Add(Line line, List<string> values, PlayerRoster roster)
            {
                var occurrence = new EventTextOccurrence { At = line.At, Text = line.Text, Values = values };
                if (line.Source != null)
                {
                    // Charmed players speak as NPCs.
                    if (roster.IsKnownPlayer(line.Source))
                    {
                        occurrence.Player = roster.Normalize(line.Source);
                    }
                    else
                    {
                        occurrence.Npc = line.Source;
                    }
                }

                occurrences.Add(occurrence);
            }

            public EventTextGroup Build()
            {
                var group = new EventTextGroup { Kind = kind };
                var template = new StringBuilder();
                var slot = 0;
                foreach (var part in parts)
                {
                    if (part.Slot == null)
                    {
                        group.Parts.Add(new EventTextPart { Text = part.Text });
                        template.Append(EventTextTemplate.Escape(part.Text!));
                        continue;
                    }

                    var index = slot++;
                    var seen = occurrences.Select(o => o.Values[index]).Distinct(StringComparer.Ordinal).ToList();
                    var variable = part.Slot == EventTextTemplate.Player || seen.Count > 1;
                    group.Parts.Add(new EventTextPart { Slot = part.Slot, Values = seen, Variable = variable });
                    template.Append(variable ? EventTextTemplate.Placeholder(part.Slot) : EventTextTemplate.Escape(seen[0]));
                }

                group.Template = template.ToString();
                group.Occurrences.AddRange(occurrences);
                group.Timer = EventTextTimer.Measure(occurrences.Select(o => o.At));
                return group;
            }
        }

        private sealed class Split
        {
            public Split(string key, List<EventTextPart> parts, List<string> values)
            {
                Key = key;
                Parts = parts;
                Values = values;
            }

            public string Key { get; }

            public List<EventTextPart> Parts { get; }

            public List<string> Values { get; }
        }

        private sealed class Line
        {
            public Line(long at, string kind, string? source, string text)
            {
                At = at;
                Kind = kind;
                Source = source;
                Text = text;
            }

            public long At { get; }

            public string Kind { get; }

            public string? Source { get; }

            public string Text { get; }
        }
    }
}

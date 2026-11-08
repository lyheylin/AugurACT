using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AugurACT.Core.EventTexts
{
    // The regex an ACT trigger needs for an event text. ACT matches triggers against the line as the game wrote it,
    // markup included, so the regex is built from a real line of the text (EventTextOccurrence.Raw, or the
    // EventTextRecord.Line saved with it):
    //
    //   \aNPC 7273717 A ghouly bladegaze:A ghouly bladegaze\/a is summoned into the area to combat your scouts!
    //   A ghouly bladegaze\\/a is summoned into the area to combat your scouts!
    //
    //   \#00FF00Thresinet targets Tanka for Flesh Decay!            with "Thresinet targets {player} for Flesh Decay!"
    //   \\#00FF00Thresinet targets (?<player>[A-Za-z]+) for Flesh Decay!
    //
    // The NPC link at the start of a line is dropped up to the name (its id changes every spawn). Links later in
    // the line keep a pattern for their id; colour codes are kept as written. Each variable is a named group,
    // numbered from the second of a kind (player, player2), so a trigger can speak it as ${player}.
    //
    // Without a line, or when the text no longer fits it, the regex is built from the text alone. An emote or
    // NPC say still gets its link end after a name the encounter lists; narration loses its colour codes, which is
    // fine at the start of a line but not in the middle. FromLine says which it was.
    public sealed class TriggerRegex
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant;
        private static readonly Regex Markup = new Regex(@"\\#[0-9A-Fa-f]{6}|\\a(?<type>[A-Z]+) -?\d+ [^\\]*?:|\\/a", Options);

        // Characters with a meaning in a .NET regex. Everything else is written as it is, so the regex reads like
        // the line (Regex.Escape would also escape spaces and #).
        private const string Special = @"\*+?|{}[]()^$.";

        private TriggerRegex(string pattern, bool fromLine)
        {
            Pattern = pattern;
            FromLine = fromLine;
        }

        public string Pattern { get; }

        // True when built from a log line, so markup is in place; false when guessed from the text.
        public bool FromLine { get; }

        // kind is an EventTextKind, template an EventTextTemplate text, line a log line's body (after the time
        // stamp) or null, and npcs the encounter's NPC names for the fallback.
        public static TriggerRegex Build(string kind, string template, string? line, IEnumerable<string>? npcs = null)
        {
            if (!EventTextTemplate.TryParse(template, out var parts, out _))
            {
                return new TriggerRegex(Escape(template ?? ""), false);
            }

            if (!string.IsNullOrEmpty(line) && TryFromLine(template, parts, line!, out var pattern))
            {
                return new TriggerRegex(pattern, true);
            }

            return new TriggerRegex(FromText(kind, parts, npcs ?? Array.Empty<string>()), false);
        }

        public static string Escape(string literal)
        {
            var text = new StringBuilder(literal.Length + 8);
            foreach (var c in literal)
            {
                if (Special.IndexOf(c) >= 0)
                {
                    text.Append('\\');
                }

                text.Append(c);
            }

            return text.ToString();
        }

        private static bool TryFromLine(string template, List<TemplatePart> parts, string line, out string pattern)
        {
            pattern = "";

            // The line as tokens: markup, or one character of the text EventTextDetector sees.
            var tokens = new List<Token>();
            var text = new StringBuilder();
            var at = 0;
            foreach (Match markup in Markup.Matches(line))
            {
                AddText(line, at, markup.Index, tokens, text);
                tokens.Add(new Token(markup.Value, markup.Groups["type"].Success ? markup.Groups["type"].Value : null));
                at = markup.Index + markup.Length;
            }

            AddText(line, at, line.Length, tokens, text);

            // The detector trims the text; the template is matched against that.
            var shown = text.ToString();
            var start = shown.Length - shown.TrimStart().Length;
            var end = shown.TrimEnd().Length;
            if (end <= start)
            {
                return false;
            }

            var match = EventTextTemplate.ToRegex(template).Match(shown.Substring(start, end - start));
            if (!match.Success)
            {
                return false;
            }

            // Where each variable is in the text, and the group it becomes.
            var spans = new List<Span>();
            var names = new GroupNames();
            var group = 1;
            foreach (var part in parts.Where(p => p.Kind != null))
            {
                var value = match.Groups[group++];
                spans.Add(new Span(start + value.Index, start + value.Index + value.Length, names.Group(part.Kind!)));
            }

            var regex = new StringBuilder();
            Span? inside = null;
            foreach (var token in tokens)
            {
                if (token.Index < 0)
                {
                    // Markup inside a variable's value can't be matched by its group.
                    if (inside != null)
                    {
                        return false;
                    }

                    if (token.LinkType == null)
                    {
                        regex.Append(token.Text.Replace(@"\", @"\\"));
                    }
                    else if (regex.Length > 0)
                    {
                        regex.Append(@"\\a").Append(token.LinkType).Append(@" -?\d+ [^\\]*?:");
                    }

                    continue;
                }

                var i = token.Index;
                if (i < start || i >= end)
                {
                    continue;
                }

                var span = spans.FirstOrDefault(s => i >= s.Start && i < s.End);
                if (span != null)
                {
                    if (i == span.Start)
                    {
                        regex.Append(span.Group);
                    }

                    inside = i + 1 < span.End ? span : null;
                    continue;
                }

                regex.Append(Escape(token.Text));
            }

            // An empty variable (a lazy match) has no character to stand in for.
            if (spans.Any(s => s.End == s.Start))
            {
                return false;
            }

            pattern = regex.ToString();
            return true;
        }

        private static void AddText(string line, int from, int to, List<Token> tokens, StringBuilder text)
        {
            for (var i = from; i < to; i++)
            {
                tokens.Add(new Token(line[i].ToString(), text.Length));
                text.Append(line[i]);
            }
        }

        private static string FromText(string kind, List<TemplatePart> parts, IEnumerable<string> npcs)
        {
            var regex = new StringBuilder();
            var names = new GroupNames();
            var speaker = kind == EventTextKind.Emote || kind == EventTextKind.Say;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Kind != null)
                {
                    regex.Append(names.Group(part.Kind));

                    // A charmed player speaks as an NPC, through a link of their name.
                    if (speaker && i == 0 && i + 1 < parts.Count && parts[1].Kind == null && parts[1].Text.StartsWith(" ", StringComparison.Ordinal))
                    {
                        regex.Append(@"\\/a");
                    }

                    continue;
                }

                var literal = part.Text;
                if (speaker && i == 0)
                {
                    // The speaker's name ends where the NPC link did; take the longest listed name that fits.
                    var name = npcs
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .OrderByDescending(n => n.Length)
                        .FirstOrDefault(n => literal.Length > n.Length && literal[n.Length] == ' '
                            && literal.StartsWith(n, StringComparison.OrdinalIgnoreCase));
                    if (name != null)
                    {
                        regex.Append(Escape(literal.Substring(0, name.Length))).Append(@"\\/a");
                        literal = literal.Substring(name.Length);
                    }
                }

                regex.Append(Escape(literal));
            }

            return regex.ToString();
        }

        private sealed class GroupNames
        {
            private readonly Dictionary<string, int> used = new Dictionary<string, int>(StringComparer.Ordinal);

            public string Group(string kind)
            {
                used.TryGetValue(kind, out var count);
                used[kind] = ++count;
                var name = count == 1 ? kind : kind + count;
                var pattern = EventTextTemplate.VariablePattern(kind);

                // Classes and archetypes are written in capitals in some lines (MAGE).
                if (kind == EventTextTemplate.Class || kind == EventTextTemplate.Archetype)
                {
                    pattern = "(?i:" + pattern + ")";
                }

                return $"(?<{name}>{pattern})";
            }
        }

        private sealed class Token
        {
            public Token(string text, int index)
            {
                Text = text;
                Index = index;
            }

            public Token(string markup, string? linkType)
            {
                Text = markup;
                Index = -1;
                LinkType = linkType;
            }

            public string Text { get; }

            // Position in the text; -1 for markup.
            public int Index { get; }

            // NPC, PC, ITEM... for the start of a link; null for a colour code or a link's end.
            public string? LinkType { get; }
        }

        private sealed class Span
        {
            public Span(int start, int end, string group)
            {
                Start = start;
                End = end;
                Group = group;
            }

            public int Start { get; }

            public int End { get; }

            public string Group { get; }
        }
    }
}

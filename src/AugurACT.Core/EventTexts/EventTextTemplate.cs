using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AugurACT.Core.EventTexts
{
    // The kinds of variable part an event text can have. A template writes them as {player}, {class},
    // {archetype} and {number}; a literal brace is doubled ("{{"). Everything else is matched as written.
    //
    //   {player} attempts to clear {player} of the Void Pox!
    //   Heroic Surge has been triggered by: {archetype}!
    //
    // This class is the only definition of the variables. The page gets them from it (EventTextConfig, sent in
    // the "config" message and written into the CLI's page), so patterns here must also be valid JavaScript.
    public static class EventTextTemplate
    {
        public const string Player = "player";
        public const string Class = "class";
        public const string Archetype = "archetype";
        public const string Number = "number";

        public static readonly IReadOnlyList<string> SlotKinds = new[] { Player, Class, Archetype, Number };

        public static readonly IReadOnlyList<string> Archetypes = new[] { "Fighter", "Priest", "Scout", "Mage" };

        public static readonly IReadOnlyList<string> Classes = new[]
        {
            "Guardian", "Berserker", "Monk", "Bruiser", "Shadowknight", "Paladin",
            "Templar", "Inquisitor", "Warden", "Fury", "Mystic", "Defiler", "Channeler",
            "Swashbuckler", "Brigand", "Troubadour", "Dirge", "Ranger", "Assassin", "Beastlord",
            "Wizard", "Warlock", "Illusionist", "Coercer", "Conjuror", "Necromancer",
        };

        // What each variable matches. Player names are one word; emotes upper-case them, so matching ignores case.
        public const string NumberPattern = @"\d+(?:[.,]\d+)*[KMBTQ]?";
        private const string PlayerPattern = @"[A-Za-z]+";

        private static readonly Regex Token = new Regex(@"\{\{|\}\}|\{(?<kind>[a-z]+)\}|[{}]", RegexOptions.CultureInvariant);

        // Splits a template into literal text and variables. Fails on an unknown {name} or a single brace.
        public static bool TryParse(string? template, out List<TemplatePart> parts, out string error)
        {
            parts = new List<TemplatePart>();
            error = "";
            if (string.IsNullOrWhiteSpace(template))
            {
                error = "The text is empty.";
                return false;
            }

            var literal = new StringBuilder();
            var at = 0;
            foreach (Match match in Token.Matches(template))
            {
                literal.Append(template, at, match.Index - at);
                at = match.Index + match.Length;
                if (match.Value == "{{" || match.Value == "}}")
                {
                    literal.Append(match.Value[0]);
                    continue;
                }

                var kind = match.Groups["kind"].Value;
                if (!SlotKinds.Contains(kind))
                {
                    error = match.Value.Length == 1
                        ? "A single { or } must be doubled, as {{ or }}."
                        : $"Unknown variable {match.Value}. Use {{player}}, {{class}}, {{archetype}} or {{number}}.";
                    return false;
                }

                Flush(literal, parts);
                parts.Add(TemplatePart.Variable(kind));
            }

            literal.Append(template, at, template!.Length - at);
            Flush(literal, parts);
            return true;
        }

        // A case-insensitive regex for whole lines (link markup and colour codes removed) that fit the template.
        // Each variable is a numbered group, in order.
        public static Regex ToRegex(string template)
        {
            if (!TryParse(template, out var parts, out var error))
            {
                throw new ArgumentException(error, nameof(template));
            }

            return new Regex(Pattern(parts), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static string Pattern(IEnumerable<TemplatePart> parts)
        {
            var pattern = new StringBuilder("^");
            foreach (var part in parts)
            {
                pattern.Append(part.Kind == null ? Regex.Escape(part.Text) : "(" + VariablePattern(part.Kind) + ")");
            }

            return pattern.Append('$').ToString();
        }

        // Parts of a line that could be variables other than player names: a class, an archetype or a number,
        // as a whole word, in named groups. The detector adds a player group; the page's dialog uses it as it is.
        public static readonly string CandidatePattern =
            "(?<![A-Za-z0-9])(?:"
            + $"(?<{Class}>(?i:{VariablePattern(Class)}))"
            + $"|(?<{Archetype}>(?i:{VariablePattern(Archetype)}))"
            + $"|(?<{Number}>{NumberPattern})"
            + ")(?![A-Za-z0-9])";

        // Player names are variables even when one name was seen; the others only when they vary.
        public static bool IsAlwaysVariable(string kind) => kind == Player;

        public static string VariablePattern(string kind)
        {
            switch (kind)
            {
                case Player: return PlayerPattern;
                case Number: return NumberPattern;
                case Class: return string.Join("|", Classes);
                case Archetype: return string.Join("|", Archetypes);
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown variable kind.");
            }
        }

        public static string Escape(string literal) => literal.Replace("{", "{{").Replace("}", "}}");

        public static string Placeholder(string kind) => "{" + kind + "}";

        private static void Flush(StringBuilder literal, List<TemplatePart> parts)
        {
            if (literal.Length > 0)
            {
                parts.Add(TemplatePart.Literal(literal.ToString()));
                literal.Clear();
            }
        }
    }

    public sealed class TemplatePart
    {
        private TemplatePart(string text, string? kind)
        {
            Text = text;
            Kind = kind;
        }

        public string Text { get; }

        // Null for literal text.
        public string? Kind { get; }

        public static TemplatePart Literal(string text) => new TemplatePart(text, null);

        public static TemplatePart Variable(string kind) => new TemplatePart("", kind);
    }
}

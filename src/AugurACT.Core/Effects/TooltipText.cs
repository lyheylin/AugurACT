using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AugurACT.Core.Effects
{
    // One line the OCR read, with the height of its words in pixels (the title is in a bigger font).
    public sealed class TooltipLine
    {
        public TooltipLine(string text, double height)
        {
            Text = text;
            Height = height;
        }

        public string Text { get; }

        public double Height { get; }
    }

    // Turns the lines OCR read from an EQ2 effect tooltip into its name and description:
    //
    //   Burning Affliction                                  name: Burning Affliction
    //   Effects:                                            description:
    //   • Inflicts 375 - 625% of max health in heat     ->    • Inflicts 375 - 625% of max health in heat damage on
    //   damage on target instantly and every second.            target instantly and every second.
    //   • Damage from this effect is harder than ...          • Damage from this effect is harder than ...
    //
    // The OCR is a draft: words are mostly right, long numbers often aren't, so the user checks it.
    public sealed class TooltipText
    {
        private static readonly Regex Header = new Regex(@"^Eff\S{0,5}\s?[:;.]?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Word = new Regex("[A-Za-z]{2,}", RegexOptions.CultureInvariant);
        private static readonly Regex CleanWord = new Regex(@"^[(""'\u2018\u201C]?[A-Za-z]+(?:'[a-z]+)?[)""'\u2019\u201D.,!?:;]*$", RegexOptions.CultureInvariant);
        private static readonly Regex CleanNumber = new Regex(@"^\d{1,3}(?:,\d{3})*(?:\.\d+)?%?[.,]?$|^\d+(?:\.\d+)?%?[.,]?$", RegexOptions.CultureInvariant);

        // How much of a reading looks like real text: letters of whole words and digits of well-formed numbers
        // count, anything else ("Infl.i&", "proF*rly", "37S") counts against it. The OCR reads each line a few
        // ways; the best scoring one is kept.
        public static int Plausibility(string? line)
        {
            var score = 0;
            foreach (var token in (line ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (CleanWord.IsMatch(token) || CleanNumber.IsMatch(token))
                {
                    score += token.Count(char.IsLetterOrDigit);
                }
                else if (token == "-" || token == "\u2013" || token.All(IsBulletMark))
                {
                    // A dash or a bullet: neither helps nor hurts.
                }
                else
                {
                    score -= 2;
                }
            }

            return score;
        }

        public string Name { get; private set; } = "";

        // One "• " item per line, wrapped lines joined.
        public string Description { get; private set; } = "";

        public static TooltipText Read(IReadOnlyList<TooltipLine> lines)
        {
            var text = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).Select(l => new TooltipLine(l.Text.Trim(), l.Height)).ToList();
            var header = text.FindIndex(l => Header.IsMatch(l.Text));

            // The title is the tallest line above "Effects:" (or among the first three). Anything else up there is
            // usually other UI showing through the tooltip.
            var top = text.Take(header >= 0 ? header : Math.Min(3, text.Count)).Select((l, i) => (Line: l, Index: i)).Where(t => Word.IsMatch(t.Line.Text)).ToList();
            var title = top.Count == 0 ? -1 : top.OrderByDescending(t => t.Line.Height).ThenBy(t => t.Index).First().Index;
            var bodyStart = header >= 0 ? header + 1 : title + 1;

            var items = new List<StringBuilder>();
            foreach (var line in text.Skip(bodyStart))
            {
                var body = StripBullet(line.Text, out var bullet);
                if (body.Length == 0)
                {
                    continue;
                }

                var last = items.LastOrDefault();
                if (last == null || bullet || (EndsSentence(last) && StartsSentence(body)))
                {
                    items.Add(new StringBuilder(body));
                }
                else
                {
                    last.Append(' ').Append(body);
                }
            }

            return new TooltipText
            {
                Name = title >= 0 ? text[title].Text : "",
                Description = string.Join("\n", items.Select(i => "\u2022 " + i)),
            };
        }

        // A bullet as OCR reads it: "•", "·", "*", "-" or a stray mark, then a space.
        private static string StripBullet(string line, out bool bullet)
        {
            var i = 0;
            while (i < line.Length && IsBulletMark(line[i]))
            {
                i++;
            }

            // "- " is a bullet, "'Burning" a quote.
            bullet = i > 0 && (line.Take(i).Any(IsBulletOnly) || (i < line.Length && char.IsWhiteSpace(line[i])));
            return bullet ? line.Substring(i).TrimStart() : line;
        }

        private static bool IsBulletMark(char c) => IsBulletOnly(c) || c == '-' || c == '*' || c == '\'' || c == '.' || c == ',';

        // Characters that are only ever a bullet at the start of a line, never text. Fonts with a symbol bullet
        // give one from Unicode's private use area.
        private static bool IsBulletOnly(char c) => c == '\u2022' || c == '\u00B7' || c == '\u25E6' || c == '\u25AA' || (c >= '\uE000' && c <= '\uF8FF');

        private static bool EndsSentence(StringBuilder text)
        {
            var c = text[text.Length - 1];
            return c == '.' || c == '!' || c == '?' || c == ':';
        }

        private static bool StartsSentence(string text) => char.IsUpper(text[0]) || char.IsDigit(text[0]) || text[0] == '"' || text[0] == '\'';
    }
}

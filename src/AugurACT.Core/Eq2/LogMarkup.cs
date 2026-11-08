using System.Text.RegularExpressions;

namespace AugurACT.Core.Eq2
{
    public static class LogMarkup
    {
        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

        private static readonly Regex Link = new Regex(@"\\a[A-Z]+ -?\d+ [^\\]*?:(?<display>[^\\]*?)\\/a", Options);
        private static readonly Regex Color = new Regex(@"\\#[0-9A-Fa-f]{6}", Options);
        private static readonly Regex LeadingNpc = new Regex(
            @"^\\aNPC (?<id>\d+) [^\\]*?:(?<name>[^\\]*?)\\/a ?(?<rest>.*)$",
            Options);

        public static string Strip(string body)
        {
            if (body.IndexOf('\\') < 0)
            {
                return body;
            }

            // Colours first: item links carry one inside their display text ("\aITEM 1 2:\#f0c0a0Ring\/a").
            return Link.Replace(Color.Replace(body, ""), "${display}");
        }

        public static bool TryParseLeadingNpc(string body, out long id, out string name, out string rest)
        {
            var match = LeadingNpc.Match(body);
            if (!match.Success)
            {
                id = 0;
                name = rest = "";
                return false;
            }

            id = long.Parse(match.Groups["id"].Value);
            name = match.Groups["name"].Value;
            rest = Strip(match.Groups["rest"].Value);
            return true;
        }
    }
}

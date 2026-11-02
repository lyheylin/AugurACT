using System;
using System.Text.RegularExpressions;

namespace AugurACT.Core.Eq2
{
    public sealed class Eq2LogLine
    {
        private static readonly Regex Format = new Regex(
            @"^\((?<epoch>\d{10})\)\[.{24}\] (?<body>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private Eq2LogLine(long epochSeconds, string body)
        {
            EpochSeconds = epochSeconds;
            Body = body;
        }

        public long EpochSeconds { get; }

        public string Body { get; }

        public DateTimeOffset Time => DateTimeOffset.FromUnixTimeSeconds(EpochSeconds);

        // Guild MOTDs and similar continuation text span lines with no timestamp, so failure is routine.
        public static bool TryParse(string line, out Eq2LogLine? parsed)
        {
            var match = Format.Match(line);
            if (!match.Success)
            {
                parsed = null;
                return false;
            }

            parsed = new Eq2LogLine(long.Parse(match.Groups["epoch"].Value), match.Groups["body"].Value);
            return true;
        }
    }
}

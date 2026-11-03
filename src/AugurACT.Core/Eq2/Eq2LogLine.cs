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

        // Reads only the "(epoch)" prefix, for lines that need a time but no further parsing.
        public static bool TryReadEpoch(string line, out long epoch)
        {
            epoch = 0;
            if (line.Length < 39 || line[0] != '(' || line[11] != ')' || line[12] != '[' || line[37] != ']')
            {
                return false;
            }

            for (var i = 1; i <= 10; i++)
            {
                var c = line[i];
                if (c < '0' || c > '9')
                {
                    epoch = 0;
                    return false;
                }

                epoch = (epoch * 10) + (c - '0');
            }

            return true;
        }

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

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AugurACT.Core.Eq2;

namespace AugurACT.Core.Encounters
{
    // Splits a saved log into fights, roughly as ACT does: a fight is a run of damage lines with no gap longer
    // than GapSeconds, and a zone change ends one. Used where ACT's encounters aren't available (the CLI).
    //
    // Lines stream to whatever open() returns for each fight, so a whole raid night never sits in memory. Lines
    // that pass keepBefore and come shortly before the first damage are passed on too, since warnings and
    // emotes can come just before a pull.
    public sealed class FightSplitter
    {
        public const int GapSeconds = 30;

        private static readonly Regex Zone = new Regex(
            @"^You have entered (?::.+?:)?(?<zone>.+)\.$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly Func<string, bool> keepBefore;
        private readonly Func<LogFight, Action<string>> open;
        private readonly Queue<(long At, string Line)> before = new Queue<(long, string)>();
        private readonly List<LogFight> done = new List<LogFight>();
        private LogFight? current;
        private Action<string>? sink;
        private string? zone;

        public FightSplitter(Func<string, bool> keepBefore, Func<LogFight, Action<string>> open)
        {
            this.keepBefore = keepBefore;
            this.open = open;
        }

        public void Feed(string rawLine)
        {
            if (!Eq2LogLine.TryReadEpoch(rawLine, out var epoch))
            {
                return;
            }

            if (rawLine.IndexOf("] You have entered ", StringComparison.Ordinal) >= 0
                && Eq2LogLine.TryParse(rawLine, out var parsed))
            {
                var match = Zone.Match(parsed!.Body);
                if (match.Success)
                {
                    Close();
                    zone = match.Groups["zone"].Value;
                    before.Clear();
                }

                return;
            }

            var isCombat = rawLine.IndexOf(" damage", StringComparison.Ordinal) >= 0;
            if (current != null && epoch - current.End > GapSeconds)
            {
                Close();
            }

            if (current == null && isCombat)
            {
                while (before.Count > 0 && before.Peek().At < epoch - GapSeconds)
                {
                    before.Dequeue();
                }

                current = new LogFight { Zone = zone, Start = before.Count > 0 ? before.Peek().At : epoch, End = epoch };
                sink = open(current);
                foreach (var held in before)
                {
                    sink(held.Line);
                }

                before.Clear();
            }

            if (current == null)
            {
                if (keepBefore(rawLine))
                {
                    before.Enqueue((epoch, rawLine));
                    while (before.Peek().At < epoch - GapSeconds)
                    {
                        before.Dequeue();
                    }
                }

                return;
            }

            if (isCombat)
            {
                current.End = Math.Max(current.End, epoch);
            }

            sink!(rawLine);
        }

        public IReadOnlyList<LogFight> Complete()
        {
            Close();
            return done;
        }

        private void Close()
        {
            if (current != null)
            {
                done.Add(current);
                current = null;
                sink = null;
            }
        }
    }

    public sealed class LogFight
    {
        public string? Zone { get; set; }

        public long Start { get; set; }

        // The last damage line.
        public long End { get; set; }
    }
}

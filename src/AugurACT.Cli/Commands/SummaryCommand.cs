using System.Diagnostics;
using System.Text.RegularExpressions;
using AugurACT.Core.Eq2;

namespace AugurACT.Cli.Commands;

// Counts event kinds and lists the lines no rule recognizes, to decide which rules to add next.
internal static class SummaryCommand
{
    public static int Run(string path)
    {
        var numbers = new Regex(@"\d[\d,\.]*[KMBTQ]?", RegexOptions.Compiled);
        var kinds = new Dictionary<LogEventKind, long>();
        var unrecognized = new Dictionary<string, long>();
        var npcLines = new Dictionary<string, long>();
        long total = 0, noTimestamp = 0;
        var timer = Stopwatch.StartNew();

        foreach (var raw in File.ReadLines(path))
        {
            total++;
            if (!Eq2LogLine.TryParse(raw, out var line))
            {
                noTimestamp++;
                continue;
            }

            var parsed = LogEventParser.Parse(line!);
            kinds[parsed.Kind] = kinds.GetValueOrDefault(parsed.Kind) + 1;

            if (parsed.Kind == LogEventKind.Unrecognized)
            {
                var template = numbers.Replace(parsed.Text, "#");
                unrecognized[template] = unrecognized.GetValueOrDefault(template) + 1;
            }
            else if (parsed.Kind is LogEventKind.NpcSpeech or LogEventKind.NpcEmote)
            {
                npcLines[parsed.Source!] = npcLines.GetValueOrDefault(parsed.Source!) + 1;
            }
        }

        Console.WriteLine($"{total:N0} lines in {timer.Elapsed.TotalSeconds:F1}s, {noTimestamp:N0} without a timestamp");
        Console.WriteLine("\nEvent kinds");
        foreach (var (kind, count) in kinds.OrderByDescending(pair => pair.Value))
        {
            Console.WriteLine($"{count,12:N0}  {kind} ({100d * count / total:F2}%)");
        }

        Console.WriteLine("\nNPCs with speech or emotes");
        foreach (var (name, count) in npcLines.OrderByDescending(pair => pair.Value).Take(25))
        {
            Console.WriteLine($"{count,12:N0}  {name}");
        }

        Console.WriteLine("\nMost common unrecognized lines");
        foreach (var (template, count) in unrecognized.OrderByDescending(pair => pair.Value).Take(60))
        {
            Console.WriteLine($"{count,12:N0}  {template}");
        }

        return 0;
    }
}

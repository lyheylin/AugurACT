using System.Collections.Concurrent;
using System.Diagnostics;

namespace AugurACT.Cli.Commands;

// Lists the event texts (emotes, NPC speech, narration) of each fight in saved logs, grouped the way the
// plugin groups them, for checking what becomes a variable. Output holds player names in the occurrences'
// values, so keep it out of the repository.
internal static class TextsCommand
{
    public static int Run(List<string> paths, DateTime? since, string? data)
    {
        var files = LogFiles.Expand(paths, since);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("no log files found");
            return 1;
        }

        var blacklist = TimelineCommand.Blacklist(data);
        var timer = Stopwatch.StartNew();
        var results = new ConcurrentBag<TimelinePage.Fight>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            foreach (var fight in TimelineCommand.Scan(file, blacklist, s => s.EventTexts.Count > 0))
            {
                results.Add(fight);
            }
        });

        var fights = results.OrderBy(f => f.Start).ThenBy(f => f.Source, StringComparer.Ordinal).ToList();
        foreach (var fight in fights)
        {
            var at = DateTimeOffset.FromUnixTimeSeconds(fight.Start).ToLocalTime();
            Console.WriteLine($"\n== {fight.Title} · {fight.Zone} · {at:yyyy-MM-dd HH:mm} · {fight.Source}");
            foreach (var group in fight.Summary.EventTexts)
            {
                var seen = group.Parts.Where(p => p.Slot != null && p.Values!.Count > 1 && p.Slot != "player")
                    .Select(p => $"{p.Slot}: {string.Join("/", p.Values!.Take(6))}");
                var extra = string.Join("; ", seen);
                var every = group.Timer == null ? "" : $"   timer {group.Timer.Mean:F0}±{group.Timer.Spread:F0}s cv {group.Timer.Variation:F3}{(group.Timer.Steady ? " STEADY" : "")} ({group.Timer.Events} events, {group.Timer.Min}-{group.Timer.Max}s)";
                Console.WriteLine($"  {group.Kind,-9} {group.Occurrences.Count,4}  {group.Template}{(extra.Length > 0 ? "   [" + extra + "]" : "")}{every}");
            }
        }

        Console.WriteLine($"\n{files.Count} files, {fights.Count} fights in {timer.Elapsed.TotalSeconds:F1}s");
        return 0;
    }
}

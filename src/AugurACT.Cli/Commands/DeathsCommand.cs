using System.Collections.Concurrent;
using System.Diagnostics;
using AugurACT.Core.Encounters;
using AugurACT.Core.Mechanics;

namespace AugurACT.Cli.Commands;

// Lists player deaths in saved logs, per character, for checking the death rules against real fights.
// Without ACT's ally list, who counts as a player comes from the log alone (see DeathDetector).
internal static class DeathsCommand
{
    public static int Run(List<string> paths, DateTime? since)
    {
        var files = LogFiles.Expand(paths, since);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("no log files found");
            return 1;
        }

        var timer = Stopwatch.StartNew();
        var results = new ConcurrentBag<(string File, IReadOnlyList<PlayerDeath> Deaths)>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            results.Add((file, Scan(file)));
        });

        var all = results.SelectMany(r => r.Deaths).ToList();
        Console.WriteLine($"{files.Count} files, {all.Count} deaths in {timer.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine("Two characters' logs of the same fight both count here.\n");

        foreach (var (file, deaths) in results.OrderBy(r => r.File, StringComparer.Ordinal))
        {
            var noKiller = deaths.Count(d => d.Killer == null);
            Console.WriteLine($"{Path.GetFileName(file),-46} {deaths.Count,5} deaths, {deaths.Select(d => d.Name).Distinct().Count(),3} players, {noKiller,4} from pain and suffering only");
        }

        Console.WriteLine($"\n{"Character",-20} {"Deaths",6} {"No killer",9}  Most deaths from");
        foreach (var group in all.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            var killers = group.Where(d => d.Killer != null)
                .GroupBy(d => d.Killer!)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{g.Key} ({g.Count()})");
            Console.WriteLine($"{group.Key,-20} {group.Count(),6} {group.Count(d => d.Killer == null),9}  {string.Join(", ", killers)}");
        }

        return 0;
    }

    // The whole file as one encounter, so the roster learns players from the whole night. Every line goes in:
    // the roster checks each for who used abilities.
    private static IReadOnlyList<PlayerDeath> Scan(string path) =>
        EncounterSummary.Build(Path.GetFileName(path), "", File.ReadLines(path), LogFiles.LoggerName(path)).Deaths;
}

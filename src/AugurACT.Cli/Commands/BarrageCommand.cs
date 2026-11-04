using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using AugurACT.Core.Eq2;
using AugurACT.Core.Mechanics;

namespace AugurACT.Cli.Commands;

// Finds barrage / bulwark casts in saved logs, prints per-boss results, and optionally updates the boss
// catalog. The timeline page with every mechanic is the "timeline" command.
internal static class BarrageCommand
{
    public static int Run(Options options)
    {
        var files = LogFiles.Expand(options.Paths, options.Since);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("no log files found");
            return 1;
        }

        var timer = Stopwatch.StartNew();
        var results = new ConcurrentBag<(string File, IReadOnlyList<BarrageCast> Casts)>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            results.Add((file, Scan(file)));
        });

        var byFile = results.OrderBy(r => r.Casts.Count > 0 ? r.Casts[0].ReadiedAt : 0).ToList();
        var all = byFile.SelectMany(r => r.Casts).ToList();
        Console.WriteLine($"{files.Count} files, {all.Count} barrage casts in {timer.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine("Two characters' logs of the same fight both count here; the catalog removes duplicates.\n");
        PrintTable(all);

        if (options.Catalog != null)
        {
            var catalog = BossCatalog.Load(options.Catalog);
            var added = catalog.AddBarrageCasts(all.Where(c => c.Zone != null));
            catalog.Save(options.Catalog);
            Console.WriteLine($"\nCatalog {options.Catalog}: {added} new casts, {catalog.WithMechanic(BossCatalog.BarrageMechanic).Count()} bosses with barrages");
        }

        return 0;
    }

    public static IReadOnlyList<BarrageCast> Scan(string path)
    {
        var detector = new BarrageDetector(LogFiles.LoggerName(path));
        foreach (var raw in File.ReadLines(path))
        {
            if (BarrageDetector.MightMatter(raw) && Eq2LogLine.TryParse(raw, out var line))
            {
                detector.Feed(line!);
            }
        }

        return detector.Complete();
    }

    private static void PrintTable(List<BarrageCast> casts)
    {
        Console.WriteLine($"{"Boss",-34} {"Zone",-46} {"Casts",5} {"Block",5} {"Miss",5} {"Cncl",5} {"Unres",5} {"Warn",5} {"Every",6}");
        var groups = casts.GroupBy(c => (c.Npc, c.Zone ?? "?")).OrderBy(g => g.Key.Item2).ThenBy(g => g.Key.Npc);
        foreach (var group in groups)
        {
            var list = group.ToList();
            var warn = Stats.Median(list.Where(c => c.Outcome != BarrageOutcome.Cancelled && c.LeadSeconds != null).Select(c => (double)c.LeadSeconds!.Value));
            var every = Stats.Median(Stats.Intervals(list.Select(c => (c.NpcId, c.ReadiedAt))));
            Console.WriteLine(
                $"{Trim(group.Key.Npc, 34),-34} {Trim(group.Key.Item2, 46),-46} {list.Count,5} " +
                $"{Count(list, BarrageOutcome.Blocked),5} {Count(list, BarrageOutcome.Missed),5} " +
                $"{Count(list, BarrageOutcome.Cancelled),5} {Count(list, BarrageOutcome.Unresolved),5} " +
                $"{Format(warn),5} {Format(every),6}");
        }
    }

    private static int Count(List<BarrageCast> casts, BarrageOutcome outcome) => casts.Count(c => c.Outcome == outcome);

    private static string Format(double? value) => value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "-";

    private static string Trim(string text, int width) => text.Length <= width ? text : text.Substring(0, width - 1) + "…";

    internal sealed class Options
    {
        public List<string> Paths { get; } = new();

        public DateTime? Since { get; set; }

        public string? Catalog { get; set; }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AugurACT.Core.Eq2;
using AugurACT.Core.Mechanics;

namespace AugurACT.Cli.Commands;

// Finds barrage / bulwark casts in saved logs, prints per-boss results, and optionally updates the boss
// catalog and writes a standalone timeline page.
internal static class BarrageCommand
{
    // Casts further apart than this, or in different zones, go in separate fights on the timeline.
    private const long FightGapSeconds = 240;

    private static readonly Regex LogName = new(@"^eq2log_(?<name>[^.]+)", RegexOptions.CultureInvariant);

    public static int Run(Options options)
    {
        var files = ExpandFiles(options.Paths, options.Since);
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

        if (options.Html != null)
        {
            var fights = byFile.SelectMany(r => Fights(Path.GetFileName(r.File), r.Casts)).ToList();
            TimelinePage.Write(options.Html, "Barrage timeline", fights);
            Console.WriteLine($"Timeline {options.Html}: {fights.Count} fights. It contains player names, so don't share or commit it.");
        }

        return 0;
    }

    public static IReadOnlyList<BarrageCast> Scan(string path)
    {
        var name = LogName.Match(Path.GetFileName(path));
        var detector = new BarrageDetector(name.Success ? name.Groups["name"].Value : null);
        foreach (var raw in File.ReadLines(path))
        {
            if (BarrageDetector.MightMatter(raw) && Eq2LogLine.TryParse(raw, out var line))
            {
                detector.Feed(line!);
            }
        }

        return detector.Complete();
    }

    private static List<string> ExpandFiles(IEnumerable<string> paths, DateTime? since)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "eq2log_*.txt"));
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }
            else
            {
                Console.Error.WriteLine($"not found: {path}");
            }
        }

        return files
            .Where(f => since == null || File.GetLastWriteTime(f) >= since)
            .Distinct()
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
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

    private static IEnumerable<TimelineFight> Fights(string source, IReadOnlyList<BarrageCast> casts)
    {
        var current = new List<BarrageCast>();
        foreach (var cast in casts)
        {
            var last = current.LastOrDefault();
            if (last != null && (last.Zone != cast.Zone || cast.ReadiedAt - (last.ResolvedAt ?? last.ReadiedAt) > FightGapSeconds))
            {
                yield return TimelineFight.From(source, current);
                current = new List<BarrageCast>();
            }

            current.Add(cast);
        }

        if (current.Count > 0)
        {
            yield return TimelineFight.From(source, current);
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

        public string? Html { get; set; }
    }
}

internal sealed class TimelineFight
{
    public string Title { get; init; } = "";

    public string? Zone { get; init; }

    public string Source { get; init; } = "";

    public long Start { get; init; }

    public long End { get; init; }

    public List<BarrageCast> Barrages { get; init; } = new();

    public static TimelineFight From(string source, List<BarrageCast> casts) => new()
    {
        Title = string.Join(", ", casts.Select(c => c.Npc).Distinct()),
        Zone = casts[0].Zone,
        Source = source,
        Start = casts.Min(c => c.ReadiedAt),
        End = casts.Max(c => c.ResolvedAt ?? c.ReadiedAt),
        Barrages = casts,
    };
}

internal static class TimelinePage
{
    // The page is the plugin's own web page, with the styles, scripts and data inlined so it opens from disk.
    public static void Write(string path, string title, List<TimelineFight> fights)
    {
        var web = Path.Combine(AppContext.BaseDirectory, "web");
        var fightJson = fights.Select(f => new
        {
            title = f.Title,
            zone = f.Zone,
            source = f.Source,
            start = f.Start,
            end = f.End,
            barrages = f.Barrages.Select(ToJson),
        });
        var data = JsonSerializer.Serialize(new { type = "barrage-report", title, fights = fightJson })
            .Replace("</", "<\\/");

        var page = File.ReadAllText(Path.Combine(web, "index.html"))
            .Replace("<link rel=\"stylesheet\" href=\"style.css\">", "<style>\n" + File.ReadAllText(Path.Combine(web, "style.css")) + "</style>")
            .Replace("<script src=\"timeline.js\"></script>", "<script>\n" + File.ReadAllText(Path.Combine(web, "timeline.js")) + "</script>")
            .Replace("<script src=\"app.js\"></script>",
                "<script type=\"application/json\" id=\"augur-data\">" + data + "</script>\n<script>\n" + File.ReadAllText(Path.Combine(web, "app.js")) + "</script>");

        // The plugin's policy only allows bundled files; inline code needs its own.
        page = Regex.Replace(page, "<meta http-equiv=\"Content-Security-Policy\"[^>]*>",
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'\">");
        File.WriteAllText(path, page);
    }

    // Same field names as the plugin's DataContract JSON, so the page reads both.
    private static object ToJson(BarrageCast c) => new
    {
        npc = c.Npc,
        npcId = c.NpcId,
        zone = c.Zone,
        content = c.Content,
        target = c.Target,
        readiedAt = c.ReadiedAt,
        resolvedAt = c.ResolvedAt,
        outcome = c.OutcomeName,
        bulwarks = c.Bulwarks.Select(b => new { by = b.By, at = b.At }),
        damageAbility = c.DamageAbility,
        targetHits = c.TargetHits,
        targetDamage = c.TargetDamage,
        splashHits = c.SplashHits,
        splashPlayers = c.SplashPlayers,
        splashDamage = c.SplashDamage,
        lines = c.Lines.Select(l => new { at = l.At, text = l.Text }),
        warningSeen = c.WarningSeen,
    };
}

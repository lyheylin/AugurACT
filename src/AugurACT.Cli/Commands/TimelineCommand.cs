using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AugurACT.Core.Encounters;

namespace AugurACT.Cli.Commands;

// Writes the plugin's page as a standalone file, with every fight in the logs that had a mechanic in it.
// Fights are cut from the log the way ACT does it (FightSplitter), and each is analyzed by the same
// EncounterSummary.Build the plugin uses, so a new mechanic shows up here without changes.
internal static class TimelineCommand
{
    // Shorter fights are trash pulls, and too many to list.
    private const long MinFightSeconds = 20;

    private static readonly Regex NpcLink = new(@"\\aNPC -?\d+ (?<name>[^:\\]+):", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static int Run(List<string> paths, DateTime? since, string html)
    {
        var files = LogFiles.Expand(paths, since);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("no log files found");
            return 1;
        }

        var timer = Stopwatch.StartNew();
        var results = new ConcurrentBag<TimelinePage.Fight>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            foreach (var fight in Scan(file))
            {
                results.Add(fight);
            }
        });

        var fights = results.OrderBy(f => f.Start).ThenBy(f => f.Source, StringComparer.Ordinal).ToList();
        TimelinePage.Write(html, "Encounter timeline", fights);
        Console.WriteLine($"{files.Count} files, {fights.Count} fights with mechanics in {timer.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"Timeline {html}. It contains player names, so don't share or commit it.");
        return 0;
    }

    private static IEnumerable<TimelinePage.Fight> Scan(string path)
    {
        var self = LogFiles.LoggerName(path);
        var open = new List<(LogFight Fight, EncounterSummary.Builder Builder, Dictionary<string, int> Npcs)>();
        var splitter = new FightSplitter(EncounterSummary.MightMatter, fight =>
        {
            var builder = new EncounterSummary.Builder("", fight.Zone ?? "", self);
            var npcs = new Dictionary<string, int>();
            open.Add((fight, builder, npcs));
            return line =>
            {
                builder.Add(line);
                if (line.Contains("\\aNPC ", StringComparison.Ordinal))
                {
                    foreach (Match m in NpcLink.Matches(line))
                    {
                        npcs[m.Groups["name"].Value] = npcs.GetValueOrDefault(m.Groups["name"].Value) + 1;
                    }
                }
            };
        });
        foreach (var raw in File.ReadLines(path))
        {
            splitter.Feed(raw);
        }

        splitter.Complete();
        foreach (var (fight, builder, npcs) in open)
        {
            if (fight.End - fight.Start < MinFightSeconds)
            {
                continue;
            }

            var summary = builder.Finish();
            if (summary.MechanicEvents > 0)
            {
                yield return new TimelinePage.Fight(Title(fight, npcs, summary.Players), fight.Zone, Path.GetFileName(path), fight.Start, fight.End, summary);
            }
        }
    }

    // ACT names a fight after its main enemy; the closest the log shows without parsing damage is the NPC that
    // emotes and speaks most. Charmed players speak as NPCs, so players are left out.
    private static string Title(LogFight fight, Dictionary<string, int> npcs, List<string> players)
    {
        var top = npcs.Where(n => !players.Contains(n.Key, StringComparer.OrdinalIgnoreCase)).OrderByDescending(n => n.Value).FirstOrDefault();
        return top.Key ?? $"Fight at {DateTimeOffset.FromUnixTimeSeconds(fight.Start).ToLocalTime():HH:mm}";
    }
}

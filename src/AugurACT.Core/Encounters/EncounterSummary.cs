using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using AugurACT.Core.Eq2;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Encounters
{
    // What the page shows for one encounter. The plugin sends it as the "analysis" message and the CLI timeline
    // embeds it, both through ToJson, so the page reads one shape.
    [DataContract]
    public sealed class EncounterSummary
    {
        [DataMember(Name = "type", Order = 0)]
        public string Type { get; private set; } = "encounter-summary";

        [DataMember(Name = "title", Order = 1)]
        public string Title { get; private set; } = "";

        [DataMember(Name = "zone", Order = 2)]
        public string Zone { get; private set; } = "";

        [DataMember(Name = "totalLines", Order = 3)]
        public int TotalLines { get; private set; }

        [DataMember(Name = "parsedLines", Order = 4)]
        public int ParsedLines { get; private set; }

        [DataMember(Name = "startEpoch", Order = 5)]
        public long? StartEpoch { get; private set; }

        [DataMember(Name = "endEpoch", Order = 6)]
        public long? EndEpoch { get; private set; }

        // Everyone in the encounter, in name order; the timeline gives each a lane.
        [DataMember(Name = "players", Order = 7)]
        public List<string> Players { get; private set; } = new List<string>();

        // One list per mechanic, filled by its detector (see MechanicSet).
        [DataMember(Name = "barrages", Order = 20)]
        public List<BarrageCast> Barrages { get; private set; } = new List<BarrageCast>();

        [DataMember(Name = "deaths", Order = 21)]
        public List<PlayerDeath> Deaths { get; private set; } = new List<PlayerDeath>();

        // How many mechanic events of any kind were found.
        public int MechanicEvents { get; private set; }

        // players is ACT's ally list; without it the log has to show who the players are.
        public static EncounterSummary Build(
            string title, string zone, IEnumerable<string> lines, string? selfName = null, IEnumerable<string>? players = null)
        {
            var builder = new Builder(title, zone, selfName, players);
            foreach (var line in lines)
            {
                builder.Add(line);
            }

            return builder.Finish();
        }

        // Whether a mechanic could need this raw line. Build also reads every line for who used abilities, so only
        // filter with this where that doesn't matter, like the lines held before a fight starts.
        public static bool MightMatter(string rawLine)
        {
            return Filter.Roster.MightMatter(rawLine) || Filter.Detectors.Any(d => d.MightMatter(rawLine));
        }

        public string ToJson()
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(EncounterSummary)).WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        // Takes an encounter's raw lines one at a time, for callers that stream them, like the CLI's fight splitter.
        public sealed class Builder
        {
            private readonly EncounterSummary summary;
            private readonly PlayerRoster roster;
            private readonly IReadOnlyList<IMechanicDetector> detectors;
            private readonly bool[] wanted;

            public Builder(string title, string zone, string? selfName = null, IEnumerable<string>? players = null)
            {
                summary = new EncounterSummary { Title = title, Zone = zone };
                roster = new PlayerRoster(selfName, players);
                detectors = MechanicSet.Create(new ScanContext(selfName, zone, roster));
                wanted = new bool[detectors.Count];
            }

            public void Add(string line)
            {
                summary.TotalLines++;
                roster.Observe(line);

                // Most lines only matter for the time range; a full parse is kept for the few that might be mechanics.
                var forRoster = roster.MightMatter(line);
                var any = forRoster;
                for (var i = 0; i < detectors.Count; i++)
                {
                    wanted[i] = detectors[i].MightMatter(line);
                    any |= wanted[i];
                }

                long epoch;
                if (any)
                {
                    if (!Eq2LogLine.TryParse(line, out var parsed))
                    {
                        return;
                    }

                    if (forRoster)
                    {
                        roster.Feed(parsed!);
                    }

                    for (var i = 0; i < detectors.Count; i++)
                    {
                        if (wanted[i])
                        {
                            detectors[i].Feed(parsed!);
                        }
                    }

                    epoch = parsed!.EpochSeconds;
                }
                else if (!Eq2LogLine.TryReadEpoch(line, out epoch))
                {
                    return;
                }

                summary.ParsedLines++;
                if (summary.StartEpoch == null || epoch < summary.StartEpoch)
                {
                    summary.StartEpoch = epoch;
                }

                if (summary.EndEpoch == null || epoch > summary.EndEpoch)
                {
                    summary.EndEpoch = epoch;
                }
            }

            public EncounterSummary Finish()
            {
                foreach (var detector in detectors)
                {
                    summary.MechanicEvents += detector.Complete(summary);
                }

                // Last, because detectors can add players they're sure of.
                summary.Players.AddRange(roster.Players());
                return summary;
            }
        }

        // MightMatter keeps no state, so one unused set of detectors answers it for every caller.
        private static class Filter
        {
            public static readonly PlayerRoster Roster = new PlayerRoster();
            public static readonly IReadOnlyList<IMechanicDetector> Detectors = MechanicSet.Create(new ScanContext(null, null, Roster));
        }
    }
}

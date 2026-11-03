using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using AugurACT.Core.Eq2;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Encounters
{
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

        [DataMember(Name = "barrages", Order = 7)]
        public List<BarrageCast> Barrages { get; private set; } = new List<BarrageCast>();

        public static EncounterSummary Build(string title, string zone, IEnumerable<string> lines, string? selfName = null)
        {
            var summary = new EncounterSummary { Title = title, Zone = zone };
            var barrages = new BarrageDetector(selfName, zone);

            foreach (var line in lines)
            {
                summary.TotalLines++;
                if (!Eq2LogLine.TryParse(line, out var parsed))
                {
                    continue;
                }

                summary.ParsedLines++;
                barrages.Feed(parsed!);
                var epoch = parsed!.EpochSeconds;
                if (summary.StartEpoch == null || epoch < summary.StartEpoch)
                {
                    summary.StartEpoch = epoch;
                }

                if (summary.EndEpoch == null || epoch > summary.EndEpoch)
                {
                    summary.EndEpoch = epoch;
                }
            }

            summary.Barrages.AddRange(barrages.Complete());
            return summary;
        }

        public string ToJson()
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(EncounterSummary)).WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}

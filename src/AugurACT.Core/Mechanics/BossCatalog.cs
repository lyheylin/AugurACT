using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AugurACT.Core.Mechanics
{
    // Which bosses have which mechanics, built up from analyzed logs. Holds no player names, so it can be
    // committed and shared.
    [DataContract]
    public sealed class BossCatalog
    {
        public const string BarrageMechanic = "barrage-bulwark";

        // Casts from two characters' logs of the same fight land within a second or two of each other.
        private const long DuplicateToleranceSeconds = 2;

        [DataMember(Name = "bosses", Order = 0)]
        public List<BossEntry> Bosses { get; set; } = new List<BossEntry>();

        public static BossCatalog Load(string path)
        {
            if (!File.Exists(path))
            {
                return new BossCatalog();
            }

            using (var stream = File.OpenRead(path))
            {
                var catalog = (BossCatalog)new DataContractJsonSerializer(typeof(BossCatalog)).ReadObject(stream);
                catalog.Bosses ??= new List<BossEntry>();
                return catalog;
            }
        }

        public void Save(string path)
        {
            Bosses.Sort((a, b) =>
            {
                var byZone = string.CompareOrdinal(a.Zone, b.Zone);
                return byZone != 0 ? byZone : string.CompareOrdinal(a.Name, b.Name);
            });

            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(BossCatalog)).WriteObject(writer, this);
                }

                File.WriteAllText(path, Encoding.UTF8.GetString(stream.ToArray()).Replace("\\/", "/") + "\n");
            }
        }

        // Returns how many casts were new to the catalog.
        public int AddBarrageCasts(IEnumerable<BarrageCast> casts)
        {
            var added = 0;
            foreach (var cast in casts)
            {
                var mechanic = Find(cast.Npc, cast.Zone ?? "", cast.Content).Mechanic(BarrageMechanic);
                if (mechanic.Record(cast))
                {
                    added++;
                }
            }

            foreach (var boss in Bosses)
            {
                foreach (var mechanic in boss.Mechanics)
                {
                    mechanic.Recompute();
                }
            }

            return added;
        }

        public IEnumerable<BossEntry> WithMechanic(string mechanic) =>
            Bosses.Where(boss => boss.Mechanics.Any(m => m.Id == mechanic));

        private BossEntry Find(string name, string zone, string content)
        {
            var boss = Bosses.FirstOrDefault(b => b.Name == name && b.Zone == zone);
            if (boss == null)
            {
                boss = new BossEntry { Name = name, Zone = zone, Content = content };
                Bosses.Add(boss);
            }

            return boss;
        }

        internal static bool IsDuplicate(List<long> sortedTimes, long time)
        {
            var index = sortedTimes.BinarySearch(time);
            if (index >= 0)
            {
                return true;
            }

            index = ~index;
            return (index < sortedTimes.Count && sortedTimes[index] - time <= DuplicateToleranceSeconds)
                || (index > 0 && time - sortedTimes[index - 1] <= DuplicateToleranceSeconds);
        }
    }

    [DataContract]
    public sealed class BossEntry
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = "";

        [DataMember(Name = "zone", Order = 1)]
        public string Zone { get; set; } = "";

        [DataMember(Name = "content", Order = 2)]
        public string Content { get; set; } = ContentKind.Unknown;

        [DataMember(Name = "mechanics", Order = 3)]
        public List<MechanicEntry> Mechanics { get; set; } = new List<MechanicEntry>();

        internal MechanicEntry Mechanic(string id)
        {
            Mechanics ??= new List<MechanicEntry>();
            var mechanic = Mechanics.FirstOrDefault(m => m.Id == id);
            if (mechanic == null)
            {
                mechanic = new MechanicEntry { Id = id };
                Mechanics.Add(mechanic);
            }

            return mechanic;
        }
    }

    [DataContract]
    public sealed class MechanicEntry
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; } = "";

        // "targeted" when the emote names a player (raids), "untargeted" otherwise (groups).
        [DataMember(Name = "variant", Order = 1)]
        public string? Variant { get; set; }

        [DataMember(Name = "damageAbility", Order = 2)]
        public string? DamageAbility { get; set; }

        [DataMember(Name = "warningSeconds", Order = 3)]
        public double? WarningSeconds { get; set; }

        [DataMember(Name = "intervalSeconds", Order = 4)]
        public double? IntervalSeconds { get; set; }

        [DataMember(Name = "casts", Order = 5)]
        public int Casts { get; set; }

        [DataMember(Name = "blocked", Order = 6)]
        public int Blocked { get; set; }

        [DataMember(Name = "missed", Order = 7)]
        public int Missed { get; set; }

        [DataMember(Name = "cancelled", Order = 8)]
        public int Cancelled { get; set; }

        [DataMember(Name = "unresolved", Order = 9)]
        public int Unresolved { get; set; }

        [DataMember(Name = "firstSeen", Order = 10)]
        public string? FirstSeen { get; set; }

        [DataMember(Name = "lastSeen", Order = 11)]
        public string? LastSeen { get; set; }

        [DataMember(Name = "notes", Order = 12, EmitDefaultValue = false)]
        public string? Notes { get; set; }

        // One entry per recorded cast, so re-analyzing a log doesn't count it twice.
        [DataMember(Name = "observations", Order = 13)]
        public List<string> ObservationText { get; set; } = new List<string>();

        public IEnumerable<Observation> Observations =>
            (ObservationText ?? new List<string>()).Select(Observation.Parse).Where(o => o != null)!;

        internal bool Record(BarrageCast cast)
        {
            ObservationText ??= new List<string>();
            var times = Observations.Select(o => o.ReadiedAt).OrderBy(t => t).ToList();
            if (BossCatalog.IsDuplicate(times, cast.ReadiedAt))
            {
                return false;
            }

            ObservationText.Add(new Observation
            {
                ReadiedAt = cast.ReadiedAt,
                NpcId = cast.NpcId,
                Outcome = cast.Outcome.ToString(),
                WarningSeconds = cast.Outcome == BarrageOutcome.Cancelled ? null : cast.LeadSeconds,
            }.ToString());

            if (cast.Target != null)
            {
                Variant = "targeted";
            }

            DamageAbility ??= cast.DamageAbility;
            return true;
        }

        internal void Recompute()
        {
            var observations = Observations.OrderBy(o => o.ReadiedAt).ToList();
            ObservationText = observations.Select(o => o.ToString()).ToList();
            Casts = observations.Count;
            Blocked = observations.Count(o => o.Outcome == nameof(BarrageOutcome.Blocked));
            Missed = observations.Count(o => o.Outcome == nameof(BarrageOutcome.Missed));
            Cancelled = observations.Count(o => o.Outcome == nameof(BarrageOutcome.Cancelled));
            Unresolved = observations.Count(o => o.Outcome == nameof(BarrageOutcome.Unresolved));
            Variant ??= "untargeted";
            WarningSeconds = Stats.Median(observations.Where(o => o.WarningSeconds != null).Select(o => (double)o.WarningSeconds!.Value));
            IntervalSeconds = Stats.Median(Stats.Intervals(observations.Select(o => (o.NpcId, o.ReadiedAt))));
            if (observations.Count > 0)
            {
                FirstSeen = Day(observations[0].ReadiedAt);
                LastSeen = Day(observations[observations.Count - 1].ReadiedAt);
            }
        }

        private static string Day(long epoch) => DateTimeOffset.FromUnixTimeSeconds(epoch).ToString("yyyy-MM-dd");
    }

    // Stored as one compact string per cast so the catalog stays readable: "<epoch> <npc id> <outcome> <warning s or ->".
    public sealed class Observation
    {
        public long ReadiedAt { get; set; }

        public long NpcId { get; set; }

        public string Outcome { get; set; } = "";

        public long? WarningSeconds { get; set; }

        public override string ToString() =>
            string.Join(" ", ReadiedAt, NpcId, Outcome, WarningSeconds?.ToString(CultureInfo.InvariantCulture) ?? "-");

        public static Observation? Parse(string text)
        {
            var parts = text.Split(' ');
            if (parts.Length < 4 || !long.TryParse(parts[0], out var at) || !long.TryParse(parts[1], out var id))
            {
                return null;
            }

            return new Observation
            {
                ReadiedAt = at,
                NpcId = id,
                Outcome = parts[2],
                WarningSeconds = long.TryParse(parts[3], out var warn) ? warn : (long?)null,
            };
        }
    }

    public static class Stats
    {
        // Casts by one NPC instance further apart than this are treated as separate pulls.
        public const long MaxIntervalSeconds = 300;

        public static double? Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0)
            {
                return null;
            }

            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        // Seconds between consecutive casts by the same NPC instance within one pull.
        public static IEnumerable<double> Intervals(IEnumerable<(long NpcId, long At)> casts)
        {
            foreach (var instance in casts.GroupBy(c => c.NpcId))
            {
                long? previous = null;
                foreach (var at in instance.Select(c => c.At).OrderBy(t => t))
                {
                    if (previous != null && at - previous.Value <= MaxIntervalSeconds)
                    {
                        yield return at - previous.Value;
                    }

                    previous = at;
                }
            }
        }
    }
}

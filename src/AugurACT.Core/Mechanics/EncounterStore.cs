using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AugurACT.Core.Mechanics
{
    // The user's own record of encounters, built from ACT fights when the user asks. An encounter is its zone
    // plus the set of main NPCs (bosses) the user picked; every other NPC in the fight is recorded as an add.
    [DataContract]
    public sealed class EncounterStore
    {
        public const string UnknownZone = "(unknown zone)";

        [DataMember(Name = "encounters", Order = 0)]
        public List<EncounterRecord> Encounters { get; set; } = new List<EncounterRecord>();

        public static EncounterStore Load(string path)
        {
            if (!File.Exists(path))
            {
                return new EncounterStore();
            }

            using (var stream = File.OpenRead(path))
            {
                var store = (EncounterStore)new DataContractJsonSerializer(typeof(EncounterStore)).ReadObject(stream);
                store.Encounters ??= new List<EncounterRecord>();
                foreach (var encounter in store.Encounters)
                {
                    encounter.Bosses ??= new List<NpcRecord>();
                    encounter.Adds ??= new List<NpcRecord>();
                }

                store.Sort();
                return store;
            }
        }

        // Written to a temporary file first, so a crash mid-write can't lose what was already recorded.
        public void Save(string path)
        {
            Sort();
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string text;
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(EncounterStore)).WriteObject(writer, this);
                }

                text = Encoding.UTF8.GetString(stream.ToArray()).Replace("\\/", "/") + "\n";
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        // Records one ACT fight under the encounter its bosses and zone identify. Mechanic flags only ever turn
        // on: a fight without a barrage release doesn't clear what an earlier one showed.
        public EncounterUpdateResult Update(
            string? zone,
            IEnumerable<string> bosses,
            IEnumerable<string> adds,
            IEnumerable<BarrageCast> casts,
            DateTime today)
        {
            var bossNames = Clean(bosses);
            if (bossNames.Count == 0)
            {
                throw new ArgumentException("An encounter needs at least one main NPC.", nameof(bosses));
            }

            var addNames = Clean(adds).Where(a => !bossNames.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
            var released = new HashSet<string>(
                casts.Where(c => c.Outcome == BarrageOutcome.Blocked || c.Outcome == BarrageOutcome.Missed).Select(c => c.Npc),
                StringComparer.OrdinalIgnoreCase);
            var day = today.ToString("yyyy-MM-dd");

            var encounter = Find(zone, bossNames);
            var result = new EncounterUpdateResult { IsNew = encounter == null };
            if (encounter == null)
            {
                encounter = new EncounterRecord { Zone = ZoneName(zone), FirstRecorded = day };
                Encounters.Add(encounter);
            }

            foreach (var name in bossNames)
            {
                Touch(encounter.Bosses, name, released);
            }

            foreach (var name in addNames)
            {
                if (Touch(encounter.Adds, name, released))
                {
                    result.AddsAdded++;
                }
            }

            encounter.Updates++;
            encounter.Updated = day;
            Sort();

            result.Zone = encounter.Zone;
            result.Name = encounter.Name;
            result.Adds = addNames.Count;
            return result;
        }

        public EncounterRecord? Find(string? zone, IEnumerable<string> bosses)
        {
            var zoneName = ZoneName(zone);
            var wanted = new HashSet<string>(Clean(bosses), StringComparer.OrdinalIgnoreCase);
            return Encounters.FirstOrDefault(e =>
                string.Equals(e.Zone, zoneName, StringComparison.OrdinalIgnoreCase)
                && wanted.SetEquals(e.Bosses.Select(b => b.Name)));
        }

        // Returns true when the NPC is new to the list.
        private static bool Touch(List<NpcRecord> list, string name, HashSet<string> released)
        {
            // ACT and the emote links can disagree on case ("a Shissar" vs "A Shissar"); keep the first spelling.
            var record = list.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
            var isNew = record == null;
            if (record == null)
            {
                record = new NpcRecord { Name = name };
                list.Add(record);
            }

            record.ReleasesBarrage |= released.Contains(name);
            return isNew;
        }

        private void Sort()
        {
            foreach (var encounter in Encounters)
            {
                encounter.Bosses.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
                encounter.Adds.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            }

            Encounters.Sort((a, b) =>
            {
                var byZone = StringComparer.OrdinalIgnoreCase.Compare(a.Zone, b.Zone);
                return byZone != 0 ? byZone : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            });
        }

        private static List<string> Clean(IEnumerable<string> names) =>
            names.Select(n => n?.Trim() ?? "")
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static string ZoneName(string? zone) => string.IsNullOrWhiteSpace(zone) ? UnknownZone : zone!.Trim();
    }

    [DataContract]
    public sealed class EncounterRecord
    {
        [DataMember(Name = "zone", Order = 0)]
        public string Zone { get; set; } = "";

        // The bosses' names, for display. Written to the file too, so it reads well by hand.
        [DataMember(Name = "name", Order = 1)]
        public string Name
        {
            get => string.Join(", ", (Bosses ?? new List<NpcRecord>()).Select(b => b.Name));
            private set { }
        }

        [DataMember(Name = "bosses", Order = 2)]
        public List<NpcRecord> Bosses { get; set; } = new List<NpcRecord>();

        // Every other NPC seen in the recorded fights.
        [DataMember(Name = "adds", Order = 3)]
        public List<NpcRecord> Adds { get; set; } = new List<NpcRecord>();

        // How many ACT fights have been recorded into this encounter.
        [DataMember(Name = "updates", Order = 4)]
        public int Updates { get; set; }

        [DataMember(Name = "firstRecorded", Order = 5)]
        public string? FirstRecorded { get; set; }

        [DataMember(Name = "updated", Order = 6)]
        public string? Updated { get; set; }
    }

    // One NPC and what is known about its mechanics. More mechanics will add fields here.
    [DataContract]
    public sealed class NpcRecord
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = "";

        // True once any recorded fight showed this NPC releasing a barrage (blocked or missed).
        [DataMember(Name = "releasesBarrage", Order = 1)]
        public bool ReleasesBarrage { get; set; }
    }

    [DataContract]
    public sealed class EncounterUpdateResult
    {
        [DataMember(Name = "zone", Order = 0)]
        public string Zone { get; set; } = "";

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; } = "";

        // False when the fight was added to an encounter already recorded.
        [DataMember(Name = "isNew", Order = 2)]
        public bool IsNew { get; set; }

        [DataMember(Name = "adds", Order = 3)]
        public int Adds { get; set; }

        // Adds not recorded for this encounter before.
        [DataMember(Name = "addsAdded", Order = 4)]
        public int AddsAdded { get; set; }
    }
}

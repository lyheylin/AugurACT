using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using AugurACT.Core.EventTexts;

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

        // Event texts never to list, in any encounter (Heroic Surge and other player or system text).
        [DataMember(Name = "eventTextBlacklist", Order = 1)]
        public List<EventTextRecord> Blacklist { get; set; } = new List<EventTextRecord>();

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
                store.Blacklist ??= new List<EventTextRecord>();
                foreach (var encounter in store.Encounters)
                {
                    encounter.Bosses ??= new List<NpcRecord>();
                    encounter.Adds ??= new List<NpcRecord>();
                    encounter.EventTexts ??= new List<EventTextRecord>();
                    foreach (var text in encounter.EventTexts.Where(t => t.Timer != null))
                    {
                        text.Timer!.Fights ??= new List<long>();
                    }
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

        // Adds an event text to a recorded encounter, found by zone and name, or updates the one already listed
        // under the same text (or under edit.Original, when the text itself was edited). True when it was new.
        public bool SaveEventText(string? zone, string? name, EventTextEdit edit, DateTime today)
        {
            var checkedText = Check(edit.Kind, edit.Text);
            var encounter = FindByName(zone, name) ?? throw new ArgumentException("That encounter isn't in the encounters data.");
            if (edit.SetTimer && edit.Timer != null && (!(edit.Timer.Mean > 0) || edit.Timer.Spread < 0))
            {
                throw new ArgumentException("A timer needs an interval above 0 s and a ± of 0 s or more.");
            }

            var record = encounter.EventTexts.FirstOrDefault(t => Same(t.Text, edit.Original ?? checkedText.Text));
            if (record != null && edit.Original != null && !Same(edit.Original, checkedText.Text)
                && encounter.EventTexts.Any(t => t != record && Same(t.Text, checkedText.Text)))
            {
                throw new ArgumentException("The encounter already lists that text.");
            }

            var isNew = record == null;
            if (record == null)
            {
                record = new EventTextRecord { Added = today.ToString("yyyy-MM-dd") };
                encounter.EventTexts.Add(record);
            }

            record.Kind = checkedText.Kind;
            record.Text = checkedText.Text;
            record.Description = string.IsNullOrWhiteSpace(edit.Description) ? null : edit.Description!.Trim();
            if (edit.SetTimer)
            {
                record.Timer = edit.Timer;
            }

            return isNew;
        }

        public bool RemoveEventText(string? zone, string? name, string? text)
        {
            var encounter = FindByName(zone, name);
            return encounter != null && encounter.EventTexts.RemoveAll(t => Same(t.Text, text)) > 0;
        }

        // Blacklists an event text and drops it from every encounter's list. False when it was already listed.
        public bool AddToBlacklist(string? kind, string? text, DateTime today)
        {
            var record = Check(kind, text);
            foreach (var encounter in Encounters)
            {
                encounter.EventTexts.RemoveAll(t => Same(t.Text, record.Text));
            }

            if (Blacklist.Any(t => Same(t.Text, record.Text)))
            {
                return false;
            }

            record.Added = today.ToString("yyyy-MM-dd");
            Blacklist.Add(record);
            return true;
        }

        public bool RemoveFromBlacklist(string? text) => Blacklist.RemoveAll(t => Same(t.Text, text)) > 0;

        // The blacklist as regexes for EncounterSummary.Build. Entries that no longer parse are skipped.
        public List<Regex> BlacklistRules()
        {
            var rules = new List<Regex>();
            foreach (var entry in Blacklist)
            {
                if (EventTextTemplate.TryParse(entry.Text, out _, out _))
                {
                    rules.Add(EventTextTemplate.ToRegex(entry.Text));
                }
            }

            return rules;
        }

        public EncounterRecord? FindByName(string? zone, string? name)
        {
            var zoneName = ZoneName(zone);
            return Encounters.FirstOrDefault(e =>
                string.Equals(e.Zone, zoneName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static EventTextRecord Check(string? kind, string? text)
        {
            if (!EventTextKind.IsKnown(kind))
            {
                throw new ArgumentException("Unknown event text kind: " + kind);
            }

            if (!EventTextTemplate.TryParse(text, out _, out var error))
            {
                throw new ArgumentException(error);
            }

            return new EventTextRecord { Kind = kind!, Text = text!.Trim() };
        }

        private static bool Same(string a, string? b) => string.Equals(a, b?.Trim(), StringComparison.Ordinal);

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

        // Emotes, NPC speech and narration the user picked as this encounter's, as EventTextTemplate texts.
        [DataMember(Name = "eventTexts", Order = 7)]
        public List<EventTextRecord> EventTexts { get; set; } = new List<EventTextRecord>();
    }

    [DataContract]
    public sealed class EventTextRecord
    {
        // emote, say or narration (EventTextKind).
        [DataMember(Name = "kind", Order = 0)]
        public string Kind { get; set; } = "";

        // The text with its variables, like "{player} is marked for death!" (EventTextTemplate).
        [DataMember(Name = "text", Order = 1)]
        public string Text { get; set; } = "";

        [DataMember(Name = "added", Order = 2, EmitDefaultValue = false)]
        public string? Added { get; set; }

        // The user's note on what the text means or what to do about it.
        [DataMember(Name = "description", Order = 3, EmitDefaultValue = false)]
        public string? Description { get; set; }

        [DataMember(Name = "timer", Order = 4, EmitDefaultValue = false)]
        public RecordedTimer? Timer { get; set; }
    }

    // How often an event text repeats, from the fights it was measured in (EventTextTimer) or typed by hand. The
    // page combines a new fight's measurement with this one before saving.
    [DataContract]
    public sealed class RecordedTimer
    {
        // Seconds between events.
        [DataMember(Name = "mean", Order = 0)]
        public double Mean { get; set; }

        // ± seconds, covering every interval seen.
        [DataMember(Name = "spread", Order = 1)]
        public double Spread { get; set; }

        // How many intervals the mean is from; 0 when typed by hand.
        [DataMember(Name = "intervals", Order = 2)]
        public int Intervals { get; set; }

        // Start epochs of the fights measured, so one fight isn't counted twice.
        [DataMember(Name = "fights", Order = 3)]
        public List<long> Fights { get; set; } = new List<long>();
    }

    // A page request to add or update one encounter's event text.
    [DataContract]
    public sealed class EventTextEdit
    {
        [DataMember(Name = "kind")]
        public string? Kind { get; set; }

        [DataMember(Name = "text")]
        public string? Text { get; set; }

        // The entry's text before an edit changed it; null when adding.
        [DataMember(Name = "original")]
        public string? Original { get; set; }

        [DataMember(Name = "description")]
        public string? Description { get; set; }

        // When false the recorded timer is left as it is; when true it becomes Timer, and null removes it.
        [DataMember(Name = "setTimer")]
        public bool SetTimer { get; set; }

        [DataMember(Name = "timer")]
        public RecordedTimer? Timer { get; set; }
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

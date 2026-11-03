using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using AugurACT.Core.Encounters;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Host
{
    // Messages between the plugin and its web page. The page sends PageRequest; the plugin answers with the
    // messages below, each tagged with "type".
    public static class HostMessages
    {
        public const string Ready = "ready";
        public const string List = "list";
        public const string Analyze = "analyze";
        public const string EncounterNpcs = "encounter-npcs";
        public const string UpdateEncounter = "update-encounter";

        public static string ToJson<T>(T message)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, message);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static PageRequest? ParseRequest(string json)
        {
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (PageRequest)new DataContractJsonSerializer(typeof(PageRequest)).ReadObject(stream);
                }
            }
            catch (SerializationException)
            {
                return null;
            }
        }
    }

    [DataContract]
    public sealed class PageRequest
    {
        [DataMember(Name = "type")]
        public string? Type { get; set; }

        // Encounter id from an EncounterInfo.
        [DataMember(Name = "id")]
        public string? Id { get; set; }

        // For "update-encounter": the NPCs the user chose as the encounter's main NPCs.
        [DataMember(Name = "bosses")]
        public List<string>? Bosses { get; set; }
    }

    // One ACT encounter, without its analysis.
    [DataContract]
    public sealed class EncounterInfo
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; } = "";

        [DataMember(Name = "title", Order = 1)]
        public string Title { get; set; } = "";

        [DataMember(Name = "zone", Order = 2)]
        public string Zone { get; set; } = "";

        [DataMember(Name = "start", Order = 3)]
        public long Start { get; set; }

        [DataMember(Name = "end", Order = 4)]
        public long End { get; set; }

        [DataMember(Name = "lines", Order = 5)]
        public int Lines { get; set; }
    }

    [DataContract]
    public sealed class EncounterListMessage
    {
        [DataMember(Name = "type", Order = 0)]
        public string Type { get; private set; } = "encounters";

        // True when the list replaces what the page shows; false when it adds to it (a newly ended encounter).
        [DataMember(Name = "replace", Order = 1)]
        public bool Replace { get; set; }

        [DataMember(Name = "items", Order = 2)]
        public List<EncounterInfo> Items { get; set; } = new List<EncounterInfo>();
    }

    [DataContract]
    public sealed class AnalysisMessage
    {
        [DataMember(Name = "type", Order = 0)]
        public string Type { get; private set; } = "analysis";

        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; } = "";

        [DataMember(Name = "summary", Order = 2)]
        public EncounterSummary? Summary { get; set; }

        [DataMember(Name = "error", Order = 3, EmitDefaultValue = false)]
        public string? Error { get; set; }
    }

    // The NPCs of one ACT fight, for the user to choose its main NPCs from.
    [DataContract]
    public sealed class EncounterNpcsMessage
    {
        [DataMember(Name = "type", Order = 0)]
        public string Type { get; private set; } = HostMessages.EncounterNpcs;

        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; } = "";

        [DataMember(Name = "zone", Order = 2)]
        public string Zone { get; set; } = "";

        // Most damage taken first, which usually puts the bosses on top.
        [DataMember(Name = "npcs", Order = 3)]
        public List<EncounterNpc> Npcs { get; set; } = new List<EncounterNpc>();

        [DataMember(Name = "error", Order = 4, EmitDefaultValue = false)]
        public string? Error { get; set; }

        // Enemies from ACT, plus any NPC that warned of a barrage without ACT counting it as a combatant.
        public static List<EncounterNpc> Collect(IEnumerable<EncounterNpc> enemies, IEnumerable<BarrageCast> casts)
        {
            var byName = new Dictionary<string, EncounterNpc>(StringComparer.OrdinalIgnoreCase);
            foreach (var enemy in enemies)
            {
                var name = enemy.Name?.Trim() ?? "";
                if (name.Length == 0)
                {
                    continue;
                }

                if (byName.TryGetValue(name, out var known))
                {
                    known.DamageTaken += enemy.DamageTaken;
                }
                else
                {
                    byName[name] = new EncounterNpc { Name = name, DamageTaken = enemy.DamageTaken };
                }
            }

            foreach (var cast in casts)
            {
                if (!byName.ContainsKey(cast.Npc))
                {
                    byName[cast.Npc] = new EncounterNpc { Name = cast.Npc };
                }
            }

            return byName.Values
                .OrderByDescending(n => n.DamageTaken)
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    [DataContract]
    public sealed class EncounterNpc
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = "";

        [DataMember(Name = "damageTaken", Order = 1)]
        public long DamageTaken { get; set; }
    }

    [DataContract]
    public sealed class EncounterDataMessage
    {
        [DataMember(Name = "type", Order = 0)]
        public string Type { get; private set; } = "encounter-data";

        [DataMember(Name = "encounters", Order = 1)]
        public List<EncounterRecord> Encounters { get; set; } = new List<EncounterRecord>();

        // Where the store lives, shown so the user can back it up.
        [DataMember(Name = "path", Order = 2)]
        public string? Path { get; set; }

        // Set when this message answers an "update-encounter" request.
        [DataMember(Name = "updatedId", Order = 3, EmitDefaultValue = false)]
        public string? UpdatedId { get; set; }

        [DataMember(Name = "result", Order = 4, EmitDefaultValue = false)]
        public EncounterUpdateResult? Result { get; set; }

        [DataMember(Name = "error", Order = 5, EmitDefaultValue = false)]
        public string? Error { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using AugurACT.Core.Effects;
using AugurACT.Core.Encounters;
using AugurACT.Core.EventTexts;
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

        // An event text's trigger to ACT: XML on the clipboard, or straight into ACT's custom triggers. Both answer
        // with a "notice".
        public const string CopyTrigger = "copy-trigger";
        public const string ImportTrigger = "import-trigger";

        // A screenshot or icon for a buff or detrimental: saved to the images folder, and for a screenshot, its
        // icon and text read. Answered with "effect-image".
        public const string AddEffectImage = "add-effect-image";

        // Requests that edit the encounters data are listed in StoreEditSet.

        public static string ToJson<T>(T message)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, message);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static PageRequest? ParseRequest(string json) => Parse<PageRequest>(json);

        // Reads a page message as T, ignoring fields T doesn't have; null when it isn't valid JSON for T.
        public static T? Parse<T>(string json)
            where T : class
        {
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
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

        // For "copy-trigger" and "import-trigger".
        [DataMember(Name = "trigger")]
        public EventTrigger? Trigger { get; set; }

        // For "add-effect-image": the page's id for the request, the image (base64, any format Windows reads),
        // and whether it is a tooltip screenshot to read, or an icon to use as it is.
        [DataMember(Name = "requestId")]
        public string? RequestId { get; set; }

        [DataMember(Name = "data")]
        public string? Data { get; set; }

        [DataMember(Name = "asIcon")]
        public bool AsIcon { get; set; }
    }

    // What became of an "add-effect-image": the saved image's file name, and for a screenshot the icon found in it
    // and the text read from it (each null when there was none).
    [DataContract]
    public sealed class EffectImageMessage
    {
        [DataMember(Name = "type", Order = 0)]
        public string Type { get; private set; } = "effect-image";

        [DataMember(Name = "requestId", Order = 1)]
        public string? RequestId { get; set; }

        [DataMember(Name = "file", Order = 2, EmitDefaultValue = false)]
        public string? File { get; set; }

        [DataMember(Name = "icon", Order = 3, EmitDefaultValue = false)]
        public string? Icon { get; set; }

        [DataMember(Name = "name", Order = 4, EmitDefaultValue = false)]
        public string? Name { get; set; }

        [DataMember(Name = "description", Order = 5, EmitDefaultValue = false)]
        public string? Description { get; set; }

        // Set when the image was saved but its text couldn't be read (no OCR language installed, say).
        [DataMember(Name = "warning", Order = 6, EmitDefaultValue = false)]
        public string? Warning { get; set; }

        [DataMember(Name = "error", Order = 7, EmitDefaultValue = false)]
        public string? Error { get; set; }
    }

    // Something to tell the user that changed no data.
    [DataContract]
    public sealed class NoticeMessage
    {
        [DataMember(Name = "type", Order = 0)]
        public string Type { get; private set; } = "notice";

        [DataMember(Name = "text", Order = 1)]
        public string Text { get; set; } = "";

        [DataMember(Name = "error", Order = 2, EmitDefaultValue = false)]
        public bool Error { get; set; }
    }

    // Rules the page needs from Core, sent once when the page starts (and written into the CLI's page), so the
    // page never keeps its own copy. A feature with such rules adds a field here.
    [DataContract]
    public sealed class PageConfigMessage
    {
        [DataMember(Name = "type", Order = 0)]
        public string Type { get; private set; } = "config";

        [DataMember(Name = "eventTexts", Order = 1)]
        public EventTextConfig EventTexts { get; set; } = EventTextConfig.Create();

        // Where the page loads buff and detrimental images from (EffectImages).
        [DataMember(Name = "effectImages", Order = 2)]
        public string EffectImages { get; set; } = $"https://{Effects.EffectImages.VirtualHost}/";
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

        [DataMember(Name = "blacklist", Order = 6)]
        public List<EventTextRecord> Blacklist { get; set; } = new List<EventTextRecord>();

        // What a StoreEditSet edit did, to show the user.
        [DataMember(Name = "notice", Order = 7, EmitDefaultValue = false)]
        public string? Notice { get; set; }

        // True when an edit changed the scan options (like the blacklist): every encounter is being analyzed
        // again, and the page should ask for the one it shows.
        [DataMember(Name = "reanalyzing", Order = 8, EmitDefaultValue = false)]
        public bool Reanalyzing { get; set; }

        // Everything the page shows from the store. Data a feature adds to EncounterStore goes here too.
        public EncounterDataMessage With(EncounterStore store)
        {
            Encounters = store.Encounters;
            Blacklist = store.Blacklist;
            return this;
        }
    }
}

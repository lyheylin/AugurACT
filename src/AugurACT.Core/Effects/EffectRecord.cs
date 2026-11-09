using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AugurACT.Core.Effects
{
    // A buff or detrimental the user recorded for an encounter (EncounterRecord.Effects), by hand: its name, the
    // text of its tooltip, the user's notes, and images saved in EffectImages' folder (named by file).
    [DataContract]
    public sealed class EffectRecord
    {
        [DataMember(Name = "kind", Order = 0)]
        public string Kind { get; set; } = EffectKind.Detrimental;

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; } = "";

        // The tooltip's text: what the effect does.
        [DataMember(Name = "description", Order = 2, EmitDefaultValue = false)]
        public string? Description { get; set; }

        // The user's own notes: how to handle it.
        [DataMember(Name = "notes", Order = 3, EmitDefaultValue = false)]
        public string? Notes { get; set; }

        [DataMember(Name = "icon", Order = 4, EmitDefaultValue = false)]
        public string? Icon { get; set; }

        [DataMember(Name = "screenshots", Order = 5)]
        public List<string> Screenshots { get; set; } = new List<string>();

        [DataMember(Name = "added", Order = 6, EmitDefaultValue = false)]
        public string? Added { get; set; }

        [DataMember(Name = "updated", Order = 7, EmitDefaultValue = false)]
        public string? Updated { get; set; }
    }

    public static class EffectKind
    {
        public const string Buff = "buff";
        public const string Detrimental = "detrimental";

        public static bool IsValid(string? kind) => kind == Buff || kind == Detrimental;
    }

    // An effect as the page's dialog sends it. Original is the name it was saved under, when editing one.
    [DataContract]
    public sealed class EffectEdit
    {
        [DataMember(Name = "original")]
        public string? Original { get; set; }

        [DataMember(Name = "kind")]
        public string? Kind { get; set; }

        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [DataMember(Name = "description")]
        public string? Description { get; set; }

        [DataMember(Name = "notes")]
        public string? Notes { get; set; }

        [DataMember(Name = "icon")]
        public string? Icon { get; set; }

        [DataMember(Name = "screenshots")]
        public List<string>? Screenshots { get; set; }
    }
}

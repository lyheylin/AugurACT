using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AugurACT.Core.EventTexts
{
    public static class EventTextKind
    {
        // \aNPC id Name:Name\/a does something.
        public const string Emote = "emote";

        // \aNPC id Name:Name\/a says, "...".
        public const string Say = "say";

        // Coloured text with no NPC link: \#FF00FFSomething happens!
        public const string Narration = "narration";

        public static bool IsKnown(string? kind) => kind == Emote || kind == Say || kind == Narration;
    }

    // Lines of one kind that are the same text apart from their variable parts, from one encounter.
    [DataContract]
    public sealed class EventTextGroup
    {
        [DataMember(Name = "kind", Order = 0)]
        public string Kind { get; set; } = "";

        // The text with player names, and any class, archetype or number that varied, written as variables
        // (see EventTextTemplate).
        [DataMember(Name = "template", Order = 1)]
        public string Template { get; set; } = "";

        // The template in pieces: literal text, and every part that could be a variable with the values seen.
        // The page lets the user turn a part into a variable or back before saving.
        [DataMember(Name = "parts", Order = 2)]
        public List<EventTextPart> Parts { get; set; } = new List<EventTextPart>();

        [DataMember(Name = "occurrences", Order = 3)]
        public List<EventTextOccurrence> Occurrences { get; set; } = new List<EventTextOccurrence>();

        // How regularly it repeats; null with fewer than EventTextTimer.MinEvents events.
        [DataMember(Name = "timer", Order = 4, EmitDefaultValue = false)]
        public EventTextTimer? Timer { get; set; }

        // The regex a trigger for the template starts from, built from the first line (TriggerRegex).
        [DataMember(Name = "regex", Order = 5)]
        public string Regex { get; set; } = "";
    }

    [DataContract]
    public sealed class EventTextPart
    {
        // Literal text; empty for a slot.
        [DataMember(Name = "text", Order = 0, EmitDefaultValue = false)]
        public string? Text { get; set; }

        // player, class, archetype or number; null for literal text.
        [DataMember(Name = "slot", Order = 1, EmitDefaultValue = false)]
        public string? Slot { get; set; }

        // Distinct values seen, in order of first appearance.
        [DataMember(Name = "values", Order = 2, EmitDefaultValue = false)]
        public List<string>? Values { get; set; }

        // Whether the default template writes this slot as a variable.
        [DataMember(Name = "variable", Order = 3, EmitDefaultValue = false)]
        public bool Variable { get; set; }
    }

    [DataContract]
    public sealed class EventTextOccurrence
    {
        [DataMember(Name = "at", Order = 0)]
        public long At { get; set; }

        // The line without link markup or colour codes.
        [DataMember(Name = "text", Order = 1)]
        public string Text { get; set; } = "";

        // Who it came from, for the timeline lane: an NPC, or a player speaking as one (charmed). Neither for
        // narration.
        [DataMember(Name = "npc", Order = 2, EmitDefaultValue = false)]
        public string? Npc { get; set; }

        [DataMember(Name = "player", Order = 3, EmitDefaultValue = false)]
        public string? Player { get; set; }

        // One value per slot in the group's parts, in order.
        [DataMember(Name = "values", Order = 4)]
        public List<string> Values { get; set; } = new List<string>();

        // The line as the game wrote it, after the time stamp: link markup and colour codes included. What an ACT
        // trigger matches against.
        [DataMember(Name = "raw", Order = 5)]
        public string Raw { get; set; } = "";

        // The players its {player} values name, as the timeline's player lanes are named: "YOU" becomes the
        // logger, and capitals the player's own spelling. In order, without repeats.
        [DataMember(Name = "players", Order = 6)]
        public List<string> Players { get; set; } = new List<string>();
    }
}

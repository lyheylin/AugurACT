using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AugurACT.Core.EventTexts
{
    // Saved in the encounters data: an encounter's event texts, and the blacklist.
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

        // A log line of the text as the game wrote it (after the time stamp), markup included, from the fight it was
        // added from, with no player's name in it (TriggerRegex.Anonymize). Trigger regexes are built from it.
        // Missing for texts typed by hand, and for a line that doesn't fit the text.
        [DataMember(Name = "line", Order = 5, EmitDefaultValue = false)]
        public string? Line { get; set; }

        // The regex a new trigger starts from (TriggerRegex), kept up to date by EventTextStore.RefreshRegex.
        [DataMember(Name = "regex", Order = 6, EmitDefaultValue = false)]
        public string? Regex { get; set; }

        // False when the regex was built from the text alone, without a line.
        [DataMember(Name = "regexFromLine", Order = 7, EmitDefaultValue = false)]
        public bool RegexFromLine { get; set; }

        // ACT triggers the user made for this text; null when there are none.
        [DataMember(Name = "triggers", Order = 8, EmitDefaultValue = false)]
        public List<EventTrigger>? Triggers { get; set; }
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

        // A log line of the text from the fight (EventTextOccurrence.Raw); replaces the recorded one when set.
        [DataMember(Name = "line")]
        public string? Line { get; set; }
    }
}

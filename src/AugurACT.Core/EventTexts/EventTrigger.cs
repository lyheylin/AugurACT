using System;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace AugurACT.Core.EventTexts
{
    // An ACT custom trigger the user made for an event text, saved under it in the encounters data
    // (EventTextRecord.Triggers). The fields are ACT's own; ToXml writes them the way ACT shares a trigger.
    [DataContract]
    public sealed class EventTrigger
    {
        // ACT's sound types (CustomTrigger.SoundType, "ST" in its XML). The page offers none and TTS.
        public const int SoundNone = 0;
        public const int SoundBeep = 1;
        public const int SoundWav = 2;
        public const int SoundTts = 3;

        [DataMember(Name = "regex", Order = 0)]
        public string Regex { get; set; } = "";

        [DataMember(Name = "sound", Order = 1)]
        public int Sound { get; set; }

        // What TTS says (a regex group as ${player}), or a wav file.
        [DataMember(Name = "soundData", Order = 2, EmitDefaultValue = false)]
        public string? SoundData { get; set; }

        // ACT's category, usually the zone name.
        [DataMember(Name = "category", Order = 3, EmitDefaultValue = false)]
        public string? Category { get; set; }

        // Only active while in the zone the category names.
        [DataMember(Name = "restrictToZone", Order = 4)]
        public bool RestrictToZone { get; set; }

        // Starts the spell timer named TimerName.
        [DataMember(Name = "timer", Order = 5)]
        public bool Timer { get; set; }

        // The timer's name, or the results tab's.
        [DataMember(Name = "timerName", Order = 6, EmitDefaultValue = false)]
        public string? TimerName { get; set; }

        // Lists each match in a results tab of its own.
        [DataMember(Name = "resultsTab", Order = 7)]
        public bool ResultsTab { get; set; }

        [DataMember(Name = "added", Order = 8, EmitDefaultValue = false)]
        public string? Added { get; set; }

        // The same trigger in every setting (not when it was added). An event text can keep several triggers, even
        // with one regex and category; ACT itself keeps one per regex and category, so importing such a pair into
        // ACT leaves the last one imported.
        public bool SameAs(EventTrigger other) =>
            string.Equals(Regex, other.Regex, StringComparison.Ordinal)
            && string.Equals(Category ?? "", other.Category ?? "", StringComparison.OrdinalIgnoreCase)
            && Sound == other.Sound
            && string.Equals(SoundData ?? "", other.SoundData ?? "", StringComparison.Ordinal)
            && RestrictToZone == other.RestrictToZone
            && Timer == other.Timer
            && string.Equals(TimerName ?? "", other.TimerName ?? "", StringComparison.Ordinal)
            && ResultsTab == other.ResultsTab;

        // Throws ArgumentException with a message for the user when ACT couldn't use it. Trims the text fields.
        public EventTrigger Checked()
        {
            if (string.IsNullOrWhiteSpace(Regex))
            {
                throw new ArgumentException("The trigger needs a regular expression.");
            }

            try
            {
                _ = new Regex(Regex);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("The regular expression isn't valid: " + ex.Message);
            }

            if (Sound < SoundNone || Sound > SoundTts)
            {
                throw new ArgumentException("Unknown sound type: " + Sound);
            }

            if (Sound == SoundTts && string.IsNullOrWhiteSpace(SoundData))
            {
                throw new ArgumentException("Write what text to speech should say.");
            }

            if (Timer && string.IsNullOrWhiteSpace(TimerName))
            {
                throw new ArgumentException("A trigger timer needs the name of the spell timer to start.");
            }

            return new EventTrigger
            {
                Regex = Regex,
                Sound = Sound,
                SoundData = Sound == SoundNone ? null : Blank(SoundData),
                Category = Blank(Category),
                RestrictToZone = RestrictToZone,
                Timer = Timer,
                TimerName = Blank(TimerName),
                ResultsTab = ResultsTab,
                Added = Added,
            };
        }

        // One trigger as ACT's XML share writes it:
        //   <Trigger R="A ghouly bladegaze&#92;&#92;/a is summoned ..." SD="Scout add" ST="3" CR="T" C="Zone [Raid]" T="F" TN="" Ta="F" />
        // ACT writes a backslash as &#92;, so the regex survives being pasted through game chat.
        public string ToXml() =>
            $"<Trigger R=\"{Encode(Regex)}\" SD=\"{Encode(SoundData)}\" ST=\"{Sound}\" CR=\"{Flag(RestrictToZone)}\" "
            + $"C=\"{Encode(Category)}\" T=\"{Flag(Timer)}\" TN=\"{Encode(TimerName)}\" Ta=\"{Flag(ResultsTab)}\" />";

        private static string Flag(bool value) => value ? "T" : "F";

        private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

        // XML's own escapes, a backslash as ACT writes it, and anything outside ASCII as a character reference.
        private static string Encode(string? text)
        {
            var xml = new StringBuilder();
            var value = text ?? "";
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    xml.Append("&#").Append(char.ConvertToUtf32(c, value[++i])).Append(';');
                    continue;
                }

                switch (c)
                {
                    case '&': xml.Append("&amp;"); break;
                    case '<': xml.Append("&lt;"); break;
                    case '>': xml.Append("&gt;"); break;
                    case '"': xml.Append("&quot;"); break;
                    case '\'': xml.Append("&apos;"); break;
                    case '\\': xml.Append("&#92;"); break;
                    default:
                        if (c < 32 || c > 126)
                        {
                            xml.Append("&#").Append((int)c).Append(';');
                        }
                        else
                        {
                            xml.Append(c);
                        }

                        break;
                }
            }

            return xml.ToString();
        }
    }
}

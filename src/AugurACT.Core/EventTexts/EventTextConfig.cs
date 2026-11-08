using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace AugurACT.Core.EventTexts
{
    // What the page needs to read and match templates the way Core does (web/eventtexts/format.js), so the rules
    // exist once, in EventTextTemplate.
    [DataContract]
    public sealed class EventTextConfig
    {
        [DataMember(Name = "variables", Order = 0)]
        public List<VariableRule> Variables { get; set; } = new List<VariableRule>();

        [DataMember(Name = "candidate", Order = 1)]
        public string Candidate { get; set; } = "";

        public static EventTextConfig Create() => new EventTextConfig
        {
            Variables = EventTextTemplate.SlotKinds
                .Select(k => new VariableRule { Name = k, Pattern = EventTextTemplate.VariablePattern(k), Always = EventTextTemplate.IsAlwaysVariable(k) })
                .ToList(),
            Candidate = EventTextTemplate.CandidatePattern,
        };
    }

    [DataContract]
    public sealed class VariableRule
    {
        // Written in templates as {name}.
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = "";

        [DataMember(Name = "pattern", Order = 1)]
        public string Pattern { get; set; } = "";

        // Always a variable (player names); the dialog doesn't let the user fix it to text.
        [DataMember(Name = "always", Order = 2)]
        public bool Always { get; set; }
    }
}

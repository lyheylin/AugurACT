using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AugurACT.Core.Mechanics
{
    [DataContract]
    public sealed class PlayerDeath
    {
        // The player's name; the logger's own deaths use the logger's name when it's known, otherwise "YOU".
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = "";

        [DataMember(Name = "at", Order = 1)]
        public long At { get; set; }

        // Null for "Alas, Player has died from pain and suffering.", which names no killer.
        [DataMember(Name = "killer", Order = 2)]
        public string? Killer { get; set; }

        // The kill and "pain and suffering" lines behind this death, in log order.
        [DataMember(Name = "lines", Order = 3)]
        public List<SourceLine> Lines { get; set; } = new List<SourceLine>();
    }
}

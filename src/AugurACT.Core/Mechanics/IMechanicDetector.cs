using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AugurACT.Core.Encounters;
using AugurACT.Core.Eq2;

namespace AugurACT.Core.Mechanics
{
    // One per mechanic. EncounterSummary.Build runs every detector from MechanicSet over an encounter's lines,
    // in log order, then asks each to write what it found into the summary.
    public interface IMechanicDetector
    {
        // A cheap check on a raw log line. Lines that fail it are never parsed or fed to this detector.
        bool MightMatter(string rawLine);

        void Feed(Eq2LogLine line);

        // Writes the results into the summary and returns how many events were found.
        int Complete(EncounterSummary summary);
    }

    // What every detector may need to know about the encounter it's reading.
    public sealed class ScanContext
    {
        public ScanContext(string? selfName, string? zone, PlayerRoster roster, IEnumerable<Regex>? eventTextBlacklist = null)
        {
            SelfName = string.IsNullOrEmpty(selfName) ? null : selfName;
            Zone = zone;
            Roster = roster;
            EventTextBlacklist = eventTextBlacklist?.ToList() ?? new List<Regex>();
        }

        // The logging character, whose own lines say "YOU".
        public string? SelfName { get; }

        public string? Zone { get; }

        public PlayerRoster Roster { get; }

        // Event texts the user never wants listed (EncounterStore.Blacklist, as EventTextTemplate regexes).
        public IReadOnlyList<Regex> EventTextBlacklist { get; }
    }
}

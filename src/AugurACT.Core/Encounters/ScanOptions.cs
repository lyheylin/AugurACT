using System.Collections.Generic;
using System.Text.RegularExpressions;
using AugurACT.Core.EventTexts;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Encounters
{
    // The user's settings that change what an analysis finds, read from the encounters data. A new setting is a
    // property here and a line in From; detectors read it from ScanContext.Options. An edit that changes one
    // says so (StoreEditResult.ChangesScanOptions) and the plugin analyzes every encounter again.
    public sealed class ScanOptions
    {
        public static readonly ScanOptions None = new ScanOptions();

        // Event texts never to list (EventTextTemplate regexes).
        public IReadOnlyList<Regex> EventTextBlacklist { get; set; } = new List<Regex>();

        public static ScanOptions From(EncounterStore store) => new ScanOptions
        {
            EventTextBlacklist = store.BlacklistRules(),
        };
    }
}

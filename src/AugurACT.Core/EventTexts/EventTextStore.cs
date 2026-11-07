using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.EventTexts
{
    // Event text edits on the encounters data: each encounter's list (EncounterRecord.EventTexts) and the
    // blacklist (EncounterStore.Blacklist). Kept out of EncounterStore, which only knows encounters and files.
    // Page requests reach these through EventTextRequests.
    public static class EventTextStore
    {
        // Adds an event text to a recorded encounter, found by zone and name, or updates the one already listed
        // under the same text (or under edit.Original, when the text itself was edited). True when it was new.
        public static bool SaveEventText(this EncounterStore store, string? zone, string? name, EventTextEdit edit, DateTime today)
        {
            var checkedText = Check(edit.Kind, edit.Text);
            var encounter = store.FindByName(zone, name) ?? throw new ArgumentException("That encounter isn't in the encounters data.");
            if (edit.SetTimer && edit.Timer != null && (!(edit.Timer.Mean > 0) || edit.Timer.Spread < 0))
            {
                throw new ArgumentException("A timer needs an interval above 0 s and a ± of 0 s or more.");
            }

            var record = encounter.EventTexts.FirstOrDefault(t => Same(t.Text, edit.Original ?? checkedText.Text));
            if (record != null && edit.Original != null && !Same(edit.Original, checkedText.Text)
                && encounter.EventTexts.Any(t => t != record && Same(t.Text, checkedText.Text)))
            {
                throw new ArgumentException("The encounter already lists that text.");
            }

            var isNew = record == null;
            if (record == null)
            {
                record = new EventTextRecord { Added = today.ToString("yyyy-MM-dd") };
                encounter.EventTexts.Add(record);
            }

            record.Kind = checkedText.Kind;
            record.Text = checkedText.Text;
            record.Description = string.IsNullOrWhiteSpace(edit.Description) ? null : edit.Description!.Trim();
            if (edit.SetTimer)
            {
                record.Timer = edit.Timer;
            }

            return isNew;
        }

        public static bool RemoveEventText(this EncounterStore store, string? zone, string? name, string? text)
        {
            var encounter = store.FindByName(zone, name);
            return encounter != null && encounter.EventTexts.RemoveAll(t => Same(t.Text, text)) > 0;
        }

        // Blacklists an event text and drops it from every encounter's list. False when it was already listed.
        public static bool AddToBlacklist(this EncounterStore store, string? kind, string? text, DateTime today)
        {
            var record = Check(kind, text);
            foreach (var encounter in store.Encounters)
            {
                encounter.EventTexts.RemoveAll(t => Same(t.Text, record.Text));
            }

            if (store.Blacklist.Any(t => Same(t.Text, record.Text)))
            {
                return false;
            }

            record.Added = today.ToString("yyyy-MM-dd");
            store.Blacklist.Add(record);
            return true;
        }

        public static bool RemoveFromBlacklist(this EncounterStore store, string? text) => store.Blacklist.RemoveAll(t => Same(t.Text, text)) > 0;

        // The blacklist as regexes for EncounterSummary.Build. Entries that no longer parse are skipped.
        public static List<Regex> BlacklistRules(this EncounterStore store)
        {
            var rules = new List<Regex>();
            foreach (var entry in store.Blacklist)
            {
                if (EventTextTemplate.TryParse(entry.Text, out _, out _))
                {
                    rules.Add(EventTextTemplate.ToRegex(entry.Text));
                }
            }

            return rules;
        }

        private static EventTextRecord Check(string? kind, string? text)
        {
            if (!EventTextKind.IsKnown(kind))
            {
                throw new ArgumentException("Unknown event text kind: " + kind);
            }

            if (!EventTextTemplate.TryParse(text, out _, out var error))
            {
                throw new ArgumentException(error);
            }

            return new EventTextRecord { Kind = kind!, Text = text!.Trim() };
        }

        private static bool Same(string a, string? b) => string.Equals(a, b?.Trim(), StringComparison.Ordinal);
    }
}

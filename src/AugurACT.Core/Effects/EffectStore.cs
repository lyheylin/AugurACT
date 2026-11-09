using System;
using System.Collections.Generic;
using System.Linq;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Effects
{
    // Edits to an encounter's buffs and detrimentals (EncounterRecord.Effects). Page requests reach these through
    // EffectRequests. Image files are written separately (EffectImages); records only name them.
    public static class EffectStore
    {
        // Adds an effect to the end of a recorded encounter's list (the user orders it, ListOrder), or updates the
        // one saved under edit.Original (or under the same name) in place. True when it was new.
        public static bool SaveEffect(this EncounterStore store, string? zone, string? name, EffectEdit edit, DateTime today)
        {
            var encounter = store.FindByName(zone, name) ?? throw new ArgumentException("That encounter isn't in the encounters data.");
            if (!EffectKind.IsValid(edit.Kind))
            {
                throw new ArgumentException("Choose buff or detrimental.");
            }

            var effectName = edit.Name?.Trim() ?? "";
            if (effectName.Length == 0)
            {
                throw new ArgumentException("The effect needs a name.");
            }

            var icon = Blank(edit.Icon);
            var screenshots = (edit.Screenshots ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if ((icon != null && !EffectImages.IsValidName(icon)) || screenshots.Any(s => !EffectImages.IsValidName(s)))
            {
                throw new ArgumentException("The page sent an image name that isn't one of AugurACT's.");
            }

            var record = Find(encounter, edit.Original ?? effectName);
            if (encounter.Effects.Any(e => e != record && Same(e.Name, effectName)))
            {
                throw new ArgumentException($"{encounter.Name} already lists an effect named {effectName}.");
            }

            var day = today.ToString("yyyy-MM-dd");
            var isNew = record == null;
            if (record == null)
            {
                record = new EffectRecord { Added = day };
                encounter.Effects.Add(record);
            }

            record.Kind = edit.Kind!;
            record.Name = effectName;
            record.Description = Blank(edit.Description);
            record.Notes = Blank(edit.Notes);
            record.Icon = icon;
            record.Screenshots = screenshots;
            record.Updated = day;
            return isNew;
        }

        public static void RemoveEffect(this EncounterStore store, string? zone, string? name, string? effect)
        {
            var encounter = store.FindByName(zone, name) ?? throw new ArgumentException("That encounter isn't in the encounters data.");
            var record = Find(encounter, effect) ?? throw new ArgumentException("That effect isn't listed for the encounter.");
            encounter.Effects.Remove(record);
        }

        // Moves an effect up (-1) or down (1) the encounter's list.
        public static void MoveEffect(this EncounterStore store, string? zone, string? name, string? effect, int offset)
        {
            var encounter = store.FindByName(zone, name) ?? throw new ArgumentException("That encounter isn't in the encounters data.");
            ListOrder.Move(encounter.Effects, e => Same(e.Name, effect), offset);
        }

        // Every image file a record names, for EffectImages.Prune.
        public static HashSet<string> ImagesInUse(this EncounterStore store)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var effect in store.Encounters.SelectMany(e => e.Effects))
            {
                if (effect.Icon != null)
                {
                    names.Add(effect.Icon);
                }

                names.UnionWith(effect.Screenshots);
            }

            return names;
        }

        private static EffectRecord? Find(EncounterRecord encounter, string? name) =>
            encounter.Effects.FirstOrDefault(e => Same(e.Name, name));

        private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
    }
}

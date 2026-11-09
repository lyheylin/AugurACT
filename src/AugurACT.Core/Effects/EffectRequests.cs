using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using AugurACT.Core.Host;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Effects
{
    // The page's buff and detrimental edits (web/effects/), registered in StoreEditSet. Images are sent before
    // these, with "add-effect-image" (HostMessages.AddEffectImage), and a record only names them.
    public static class EffectRequests
    {
        public static readonly IReadOnlyList<IStoreEdit> All = new IStoreEdit[]
        {
            new StoreEdit<EffectRequest>("save-effect", (r, store, today) =>
            {
                var entry = r.Entry ?? new EffectEdit();
                var isNew = store.SaveEffect(r.Zone, r.Name, entry, today);
                var what = entry.Kind == EffectKind.Buff ? "Buff" : "Detrimental";
                return new StoreEditResult(isNew ? $"{what} added to {r.Name}." : $"{what} updated in {r.Name}.");
            }),
            new StoreEdit<EffectRequest>("remove-effect", (r, store, today) =>
            {
                store.RemoveEffect(r.Zone, r.Name, r.Effect);
                return new StoreEditResult($"{r.Effect} removed from {r.Name}.");
            }),
            // No notice: the list shows the move.
            new StoreEdit<EffectRequest>("move-effect", (r, store, today) =>
            {
                var encounter = store.FindByName(r.Zone, r.Name) ?? throw new ArgumentException("That encounter isn't in the encounters data.");
                ListOrder.Move(encounter.Effects, e => string.Equals(e.Name, r.Effect, StringComparison.OrdinalIgnoreCase), r.Offset);
                return new StoreEditResult("");
            }),
        };
    }

    // The recorded encounter (zone and name, as in EncounterRecord), and the effect: the entry to save, or the
    // name of the one to remove or move.
    [DataContract]
    public sealed class EffectRequest
    {
        [DataMember(Name = "zone")]
        public string? Zone { get; set; }

        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [DataMember(Name = "effect")]
        public string? Effect { get; set; }

        [DataMember(Name = "entry")]
        public EffectEdit? Entry { get; set; }

        // For "move-effect": -1 moves the effect up a place, 1 down.
        [DataMember(Name = "offset")]
        public int Offset { get; set; }
    }
}

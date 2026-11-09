using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using AugurACT.Core.Host;

namespace AugurACT.Core.Mechanics
{
    // The page's edits to a recorded encounter's own lists (web/app.js), registered in StoreEditSet.
    public static class EncounterRequests
    {
        public static readonly IReadOnlyList<IStoreEdit> All = new IStoreEdit[]
        {
            // An add seen in a later fight is recorded again.
            new StoreEdit<EncounterRequest>("remove-add", (r, store, today) =>
            {
                var encounter = store.FindByName(r.Zone, r.Name) ?? throw new ArgumentException("That encounter isn't in the encounters data.");
                var add = encounter.Adds.Find(a => string.Equals(a.Name, r.Add?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (add == null)
                {
                    return new StoreEditResult($"{encounter.Name} doesn't list {r.Add}.");
                }

                encounter.Adds.Remove(add);
                return new StoreEditResult($"{add.Name} removed from {encounter.Name}.");
            }),
        };
    }

    // The recorded encounter (zone and name, as in EncounterRecord) and the add's name.
    [DataContract]
    public sealed class EncounterRequest
    {
        [DataMember(Name = "zone")]
        public string? Zone { get; set; }

        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [DataMember(Name = "add")]
        public string? Add { get; set; }
    }
}

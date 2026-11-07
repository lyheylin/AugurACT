using System.Collections.Generic;
using System.Runtime.Serialization;
using AugurACT.Core.Host;

namespace AugurACT.Core.EventTexts
{
    // The page's event text edits (web/eventtexts/), registered in StoreEditSet.
    public static class EventTextRequests
    {
        public static readonly IReadOnlyList<IStoreEdit> All = new IStoreEdit[]
        {
            new StoreEdit<EventTextRequest>("add-event-text", (r, store, today) => new StoreEditResult(
                store.SaveEventText(r.Zone, r.Name, r.Entry ?? new EventTextEdit(), today)
                    ? $"Event text added to {r.Name}."
                    : $"Event text updated in {r.Name}.")),
            new StoreEdit<EventTextRequest>("remove-event-text", (r, store, today) =>
            {
                store.RemoveEventText(r.Zone, r.Name, r.Text);
                return new StoreEditResult($"Event text removed from {r.Name}.");
            }),
            new StoreEdit<EventTextRequest>("blacklist-event-text", (r, store, today) => new StoreEditResult(
                store.AddToBlacklist(r.Kind, r.Text, today)
                    ? "Event text blacklisted. Encounters are being analyzed again without it."
                    : "That event text was already blacklisted.",
                changesScanOptions: true)),
            new StoreEdit<EventTextRequest>("unblacklist-event-text", (r, store, today) =>
            {
                store.RemoveFromBlacklist(r.Text);
                return new StoreEditResult("Event text removed from the blacklist. Encounters are being analyzed again.", changesScanOptions: true);
            }),
        };
    }

    // One shape for all four: the recorded encounter (zone and name, as in EncounterRecord) where it applies,
    // the text and kind, and for "add-event-text" the entry to save.
    [DataContract]
    public sealed class EventTextRequest
    {
        [DataMember(Name = "zone")]
        public string? Zone { get; set; }

        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [DataMember(Name = "kind")]
        public string? Kind { get; set; }

        [DataMember(Name = "text")]
        public string? Text { get; set; }

        [DataMember(Name = "entry")]
        public EventTextEdit? Entry { get; set; }
    }
}

using System;
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
            // No notice: the list shows the move.
            new StoreEdit<EventTextRequest>("move-event-text", (r, store, today) =>
            {
                store.MoveEventText(r.Zone, r.Name, r.Text, r.Offset);
                return new StoreEditResult("");
            }),
            new StoreEdit<EventTextRequest>("add-event-trigger", (r, store, today) =>
            {
                var trigger = r.Trigger ?? throw new ArgumentException("The page sent no trigger.");
                switch (store.SaveTrigger(r.Zone, r.Name, r.Entry ?? new EventTextEdit(), trigger, today))
                {
                    case EventTextStore.TriggerSaved.AddedWithText: return new StoreEditResult($"Event text and its trigger added to {r.Name}.");
                    case EventTextStore.TriggerSaved.AlreadyThere: return new StoreEditResult($"{r.Name} already has that trigger.");
                    default: return new StoreEditResult($"Trigger added to {r.Name}.");
                }
            }),
            new StoreEdit<EventTextRequest>("remove-event-trigger", (r, store, today) =>
            {
                store.RemoveTrigger(r.Zone, r.Name, r.Text, r.Trigger);
                return new StoreEditResult($"Trigger removed from {r.Name}.");
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

    // One shape for all of them: the recorded encounter (zone and name, as in EncounterRecord) where it applies,
    // the text and kind, for "add-event-text" the entry to save, for "move-event-text" the offset, and for the
    // trigger edits the trigger (and for "add-event-trigger", the text it belongs to as the entry).
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

        [DataMember(Name = "trigger")]
        public EventTrigger? Trigger { get; set; }

        // For "move-event-text": -1 moves the text up a place, 1 down.
        [DataMember(Name = "offset")]
        public int Offset { get; set; }
    }
}

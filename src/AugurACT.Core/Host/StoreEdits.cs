using System;
using System.Collections.Generic;
using System.Linq;
using AugurACT.Core.Effects;
using AugurACT.Core.EventTexts;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Host
{
    // Page requests that edit the encounters data. Each feature declares its own (see EventTextRequests) and is
    // listed here once; the plugin runs whichever matches a request's "type", saves the store and answers with
    // "encounter-data", so a new kind of edit needs no plugin code. Throw ArgumentException for a message the
    // user should see.
    public static class StoreEditSet
    {
        public static readonly IReadOnlyList<IStoreEdit> All = new IStoreEdit[0]
            .Concat(EventTextRequests.All)
            .Concat(EffectRequests.All)
            .ToList();

        private static readonly Dictionary<string, IStoreEdit> ByType = All.ToDictionary(e => e.Type, StringComparer.Ordinal);

        public static bool TryGet(string? type, out IStoreEdit edit)
        {
            edit = null!;
            return type != null && ByType.TryGetValue(type, out edit!);
        }
    }

    public interface IStoreEdit
    {
        string Type { get; }

        // json is the whole page message.
        StoreEditResult Apply(string json, EncounterStore store, DateTime today);
    }

    // An edit whose page message reads as T, a [DataContract] class.
    public sealed class StoreEdit<T> : IStoreEdit
        where T : class
    {
        private readonly Func<T, EncounterStore, DateTime, StoreEditResult> apply;

        public StoreEdit(string type, Func<T, EncounterStore, DateTime, StoreEditResult> apply)
        {
            Type = type;
            this.apply = apply;
        }

        public string Type { get; }

        public StoreEditResult Apply(string json, EncounterStore store, DateTime today)
        {
            var request = HostMessages.Parse<T>(json) ?? throw new ArgumentException("The page sent an unreadable request.");
            return apply(request, store, today);
        }
    }

    public sealed class StoreEditResult
    {
        public StoreEditResult(string notice, bool changesScanOptions = false)
        {
            Notice = notice;
            ChangesScanOptions = changesScanOptions;
        }

        // Shown to the user.
        public string Notice { get; }

        // True when the edit changed what ScanOptions.From reads, so every encounter must be analyzed again.
        public bool ChangesScanOptions { get; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Advanced_Combat_Tracker;
using AugurACT.Core.Encounters;
using AugurACT.Core.Host;
using AugurACT.Core.Mechanics;

namespace AugurACT.Plugin
{
    // Connects ACT's encounter list to the page: lists encounters, analyzes them on one background thread,
    // and records fights into the encounter store when the user asks.
    //
    // Fields here hold only reference types. ACT calls GetTypes() before the assembly resolver exists, and a
    // value-type field of a Core type would make that fail (see docs/ARCHITECTURE.md).
    internal sealed class EncounterBridge : IDisposable
    {
        private readonly Action<string> post;
        private readonly Action<string> setStatus;
        private readonly string storePath;
        private readonly object gate = new object();
        // User requests go first; background analysis fills in the list's counts when nothing else is waiting.
        private readonly Queue<Action> urgentJobs = new Queue<Action>();
        private readonly Queue<Action> backgroundJobs = new Queue<Action>();
        private readonly Dictionary<string, WeakReference> encounters = new Dictionary<string, WeakReference>();
        private readonly Dictionary<string, EncounterSummary> summaries = new Dictionary<string, EncounterSummary>();
        private readonly Thread worker;
        private EncounterStore? store;
        private bool stopping;

        public EncounterBridge(Action<string> post, Action<string> setStatus, string storePath)
        {
            this.post = post;
            this.setStatus = setStatus;
            this.storePath = storePath;
            worker = new Thread(Work) { IsBackground = true, Name = "AugurACT analysis", Priority = ThreadPriority.BelowNormal };
            worker.Start();
        }

        // Called on the UI thread with a message from the page.
        public void OnPageMessage(string json)
        {
            var request = HostMessages.ParseRequest(json);
            switch (request?.Type)
            {
                case HostMessages.Ready:
                case HostMessages.List:
                    SendList();
                    Enqueue(SendEncounterData, urgent: true);
                    break;
                case HostMessages.Analyze when request.Id != null:
                    var id = request.Id;
                    Enqueue(() => Analyze(id, force: true), urgent: true);
                    break;
                case HostMessages.EncounterNpcs when request.Id != null:
                    var npcsId = request.Id;
                    Enqueue(() => SendEncounterNpcs(npcsId), urgent: true);
                    break;
                case HostMessages.UpdateEncounter when request.Id != null:
                    var updateId = request.Id;
                    var bosses = request.Bosses ?? new List<string>();
                    Enqueue(() => UpdateEncounter(updateId, bosses), urgent: true);
                    break;
            }
        }

        // Called on ACT's parse thread when an encounter ends.
        public void OnCombatEnd(EncounterData encounter)
        {
            var info = Describe(encounter);
            if (info == null)
            {
                return;
            }

            lock (gate)
            {
                encounters[info.Id] = new WeakReference(encounter);
            }

            post(HostMessages.ToJson(new EncounterListMessage { Replace = false, Items = { info } }));
            var id = info.Id;
            Enqueue(() => Analyze(id, force: false), urgent: true);
        }

        public void Dispose()
        {
            lock (gate)
            {
                stopping = true;
                urgentJobs.Clear();
                backgroundJobs.Clear();
                Monitor.PulseAll(gate);
            }
        }

        private void SendList()
        {
            var list = new EncounterListMessage { Replace = true };
            var found = new Dictionary<string, WeakReference>();
            foreach (var encounter in AllEncounters())
            {
                var info = Describe(encounter);
                if (info != null && !found.ContainsKey(info.Id))
                {
                    found[info.Id] = new WeakReference(encounter);
                    list.Items.Add(info);
                }
            }

            list.Items.Sort((a, b) => b.Start.CompareTo(a.Start));
            lock (gate)
            {
                encounters.Clear();
                foreach (var pair in found)
                {
                    encounters[pair.Key] = pair.Value;
                }

                foreach (var gone in summaries.Keys.Where(k => !found.ContainsKey(k)).ToList())
                {
                    summaries.Remove(gone);
                }
            }

            post(HostMessages.ToJson(list));

            // Fill in barrage counts for the whole list, newest first, without holding up user requests.
            lock (gate)
            {
                backgroundJobs.Clear();
            }

            foreach (var info in list.Items)
            {
                var id = info.Id;
                Enqueue(() => Analyze(id, force: false), urgent: false);
            }

            setStatus($"{list.Items.Count} encounters listed");
        }

        private void Analyze(string id, bool force)
        {
            var message = new AnalysisMessage { Id = id };
            try
            {
                var encounter = Lookup(id);
                var cached = encounter == null ? null : Cached(id, encounter);

                // Background passes skip what's current; a click always answers, from the cache if it can.
                if (cached != null && !force)
                {
                    return;
                }

                message.Summary = cached ?? (encounter == null ? null : Summarize(id, encounter));
                if (message.Summary == null)
                {
                    message.Error = "ACT no longer has this encounter. Refresh the list.";
                }
            }
            catch (Exception ex)
            {
                message.Error = "Analysis failed: " + ex.Message;
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "AugurACT: encounter analysis failed");
            }

            post(HostMessages.ToJson(message));
        }

        // A summary is only current while ACT hasn't added lines to the encounter since it was made.
        private EncounterSummary? Cached(string id, EncounterData encounter)
        {
            lock (gate)
            {
                return summaries.TryGetValue(id, out var summary) && summary.TotalLines == encounter.LogLines.Count
                    ? summary
                    : null;
            }
        }

        private EncounterSummary Summarize(string id, EncounterData encounter)
        {
            var lines = CopyLines(encounter);
            var self = string.IsNullOrEmpty(encounter.CharName) ? ActGlobals.charName : encounter.CharName;
            var summary = EncounterSummary.Build(encounter.Title, encounter.ZoneName, lines, self, Allies(encounter));
            lock (gate)
            {
                summaries[id] = summary;
            }

            // ACT keeps adding lines after it reports combat end, especially while importing. Look again once
            // the queue is clear; the line count shows whether anything changed.
            if (encounter.LogLines.Count != lines.Count)
            {
                Enqueue(() => Analyze(id, force: false), urgent: false);
            }

            return summary;
        }

        // ACT appends to LogLines on its parse thread. An enumerator throws when that happens, so copy by index
        // up to the count seen at the start; entries already there don't change.
        private static List<string> CopyLines(EncounterData encounter)
        {
            var source = encounter.LogLines;
            var count = source.Count;
            var lines = new List<string>(count);
            for (var i = 0; i < count && i < source.Count; i++)
            {
                lines.Add(source[i].LogLine);
            }

            return lines;
        }

        private void SendEncounterNpcs(string id)
        {
            var message = new EncounterNpcsMessage { Id = id };
            try
            {
                var encounter = Lookup(id);
                var summary = encounter == null ? null : Cached(id, encounter) ?? Summarize(id, encounter);
                if (encounter == null || summary == null)
                {
                    message.Error = "ACT no longer has this encounter. Refresh the list.";
                }
                else
                {
                    message.Zone = encounter.ZoneName ?? "";
                    message.Npcs = EncounterNpcsMessage.Collect(Enemies(encounter), summary.Barrages);
                }
            }
            catch (Exception ex)
            {
                message.Error = "Couldn't list this encounter's NPCs: " + ex.Message;
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "AugurACT: listing NPCs failed");
            }

            post(HostMessages.ToJson(message));
        }

        private void UpdateEncounter(string id, List<string> bosses)
        {
            var message = new EncounterDataMessage { UpdatedId = id, Path = storePath };
            try
            {
                var encounter = Lookup(id);
                var summary = encounter == null ? null : Cached(id, encounter) ?? Summarize(id, encounter);
                if (encounter == null || summary == null)
                {
                    message.Error = "ACT no longer has this encounter. Refresh the list.";
                }
                else if (bosses.Count == 0)
                {
                    message.Error = "Choose at least one main NPC.";
                }
                else
                {
                    var npcs = EncounterNpcsMessage.Collect(Enemies(encounter), summary.Barrages).Select(n => n.Name);
                    var current = Store();
                    message.Result = current.Update(encounter.ZoneName, bosses, npcs, summary.Barrages, DateTime.Now);
                    current.Save(storePath);
                    setStatus($"Encounters data updated: {message.Result.Name}");
                }
            }
            catch (Exception ex)
            {
                message.Error = "Updating encounters data failed: " + ex.Message;
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "AugurACT: encounters data update failed");
            }

            message.Encounters = Store().Encounters;
            post(HostMessages.ToJson(message));
        }

        private void SendEncounterData()
        {
            var message = new EncounterDataMessage { Path = storePath };
            try
            {
                message.Encounters = Store().Encounters;
            }
            catch (Exception ex)
            {
                message.Error = "Couldn't read encounters data: " + ex.Message;
            }

            post(HostMessages.ToJson(message));
        }

        // Loaded on first use; only the worker thread touches it.
        private EncounterStore Store()
        {
            if (store == null)
            {
                try
                {
                    store = EncounterStore.Load(storePath);
                }
                catch (Exception ex)
                {
                    // Keep the unreadable file for the user rather than overwriting it on the next save.
                    var aside = storePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    File.Move(storePath, aside);
                    setStatus("Encounters data file was unreadable; moved to " + Path.GetFileName(aside));
                    ActGlobals.oFormActMain.WriteExceptionLog(ex, "AugurACT: encounters data unreadable");
                    store = new EncounterStore();
                }
            }

            return store;
        }

        // ACT's allies decide whose deaths count. ACT changes the list on its parse thread, so a failed read
        // falls back to working it out from the log.
        private static List<string>? Allies(EncounterData encounter)
        {
            try
            {
                return encounter.GetAllies().Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NullReferenceException)
            {
                return null;
            }
        }

        // Combatants ACT doesn't count as allies, that took part in the fight.
        private static List<EncounterNpc> Enemies(EncounterData encounter)
        {
            var allies = new HashSet<string>(encounter.GetAllies().Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            return encounter.Items.Values
                .Where(c => !allies.Contains(c.Name)
                    && !string.IsNullOrWhiteSpace(c.Name)
                    && !string.Equals(c.Name, "Unknown", StringComparison.OrdinalIgnoreCase)
                    && (c.Damage > 0 || c.DamageTaken > 0))
                .Select(c => new EncounterNpc { Name = c.Name, DamageTaken = c.DamageTaken })
                .ToList();
        }

        private EncounterData? Lookup(string id)
        {
            lock (gate)
            {
                if (encounters.TryGetValue(id, out var weak) && weak.Target is EncounterData known)
                {
                    return known;
                }
            }

            foreach (var encounter in AllEncounters())
            {
                if (Id(encounter) == id)
                {
                    return encounter;
                }
            }

            return null;
        }

        // ACT adds to these lists on its parse thread, so a copy can fail mid-way; try again a few times.
        private static List<EncounterData> AllEncounters()
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var result = new List<EncounterData>();
                    foreach (var zone in ActGlobals.oFormActMain.ZoneList.ToArray())
                    {
                        result.AddRange(zone.Items.ToArray());
                    }

                    return result;
                }
                catch (Exception ex) when (attempt < 3 && (ex is InvalidOperationException || ex is ArgumentException))
                {
                    Thread.Sleep(20);
                }
            }
        }

        private static EncounterInfo? Describe(EncounterData encounter)
        {
            // Each zone's merged "All" entry repeats its encounters; the active one is still being parsed.
            if (encounter.Active || string.Equals(encounter.Title, "All", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var lines = encounter.LogLines?.Count ?? 0;
            if (lines == 0)
            {
                return null;
            }

            return new EncounterInfo
            {
                Id = Id(encounter),
                Title = encounter.Title ?? "",
                Zone = encounter.ZoneName ?? "",
                Start = Epoch(encounter.StartTime),
                End = Epoch(encounter.EndTime),
                Lines = lines,
            };
        }

        private static string Id(EncounterData encounter)
        {
            var zoneStart = encounter.Parent?.StartTime.Ticks ?? 0;
            return $"{zoneStart:x}-{encounter.StartTime.Ticks:x}-{encounter.EncId}";
        }

        private static long Epoch(DateTime time) =>
            time == DateTime.MinValue ? 0 : new DateTimeOffset(time).ToUnixTimeSeconds();

        private void Enqueue(Action job, bool urgent)
        {
            lock (gate)
            {
                if (stopping)
                {
                    return;
                }

                (urgent ? urgentJobs : backgroundJobs).Enqueue(job);
                Monitor.Pulse(gate);
            }
        }

        private void Work()
        {
            while (true)
            {
                Action job;
                lock (gate)
                {
                    while (urgentJobs.Count == 0 && backgroundJobs.Count == 0 && !stopping)
                    {
                        Monitor.Wait(gate);
                    }

                    if (stopping)
                    {
                        return;
                    }

                    job = urgentJobs.Count > 0 ? urgentJobs.Dequeue() : backgroundJobs.Dequeue();
                }

                try
                {
                    job();
                }
                catch (Exception ex)
                {
                    ActGlobals.oFormActMain.WriteExceptionLog(ex, "AugurACT: background job failed");
                }
            }
        }
    }
}

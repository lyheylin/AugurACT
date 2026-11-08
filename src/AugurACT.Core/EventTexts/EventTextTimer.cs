using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace AugurACT.Core.EventTexts
{
    // How regularly an event text repeats in one encounter.
    //
    // Lines of a group closer together than SameEventSeconds are one event (an AoE text logged once per player).
    // With at least MinEvents events, the intervals between them are measured. The timer counts as steady when
    // their coefficient of variation, stdev / mean, is at most SteadyMaxVariation: the spread relative to the
    // timer's length, so a 30 s timer and a 4 min one are judged alike. A boss on a 220 s timer that floats by
    // up to 10 s has about 0.04; player-triggered text like Heroic Surge is far above 0.3.
    //
    // A missed line (one interval twice as long) or a phase where the boss stops will fail the test. That is
    // deliberate for now: tune SteadyMaxVariation against real fights before making it more forgiving.
    [DataContract]
    public sealed class EventTextTimer
    {
        public const int SameEventSeconds = 2;
        public const int MinEvents = 4;
        public const double SteadyMaxVariation = 0.15;

        // Events after merging lines that came together.
        [DataMember(Name = "events", Order = 0)]
        public int Events { get; set; }

        // Mean interval between events, in seconds.
        [DataMember(Name = "mean", Order = 1)]
        public double Mean { get; set; }

        // The largest distance of any interval from the mean, so mean ± spread covers every interval seen.
        [DataMember(Name = "spread", Order = 2)]
        public double Spread { get; set; }

        [DataMember(Name = "min", Order = 3)]
        public long Min { get; set; }

        [DataMember(Name = "max", Order = 4)]
        public long Max { get; set; }

        // Sample standard deviation of the intervals divided by their mean.
        [DataMember(Name = "variation", Order = 5)]
        public double Variation { get; set; }

        [DataMember(Name = "steady", Order = 6)]
        public bool Steady { get; set; }

        // Null when there are fewer than MinEvents events. times must be in order.
        public static EventTextTimer? Measure(IEnumerable<long> times, double steadyMaxVariation = SteadyMaxVariation)
        {
            var events = new List<long>();
            long? previous = null;
            foreach (var t in times)
            {
                if (previous == null || t - previous.Value >= SameEventSeconds)
                {
                    events.Add(t);
                }

                previous = t;
            }

            if (events.Count < MinEvents)
            {
                return null;
            }

            var intervals = new List<long>();
            for (var i = 1; i < events.Count; i++)
            {
                intervals.Add(events[i] - events[i - 1]);
            }

            var mean = intervals.Average();
            var variance = intervals.Sum(x => (x - mean) * (x - mean)) / (intervals.Count - 1);
            var variation = mean > 0 ? Math.Sqrt(variance) / mean : double.PositiveInfinity;
            return new EventTextTimer
            {
                Events = events.Count,
                Mean = Math.Round(mean, 1),
                Spread = Math.Round(intervals.Max(x => Math.Abs(x - mean)), 1),
                Min = intervals.Min(),
                Max = intervals.Max(),
                Variation = Math.Round(variation, 3),
                Steady = variation <= steadyMaxVariation,
            };
        }
    }
}

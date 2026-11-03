using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AugurACT.Core.Mechanics
{
    public enum BarrageOutcome
    {
        // Barrage damage reached no one but the named target (raids), or no one at all (groups).
        Blocked,

        // Barrage damage reached players other than the named target.
        Missed,

        // "cancels the barrage, preferring to perform another task!"
        Cancelled,

        // Readied but never released or cancelled, such as when the boss died or the group wiped.
        Unresolved,
    }

    [DataContract]
    public sealed class BarrageCast
    {
        [DataMember(Name = "npc", Order = 0)]
        public string Npc { get; set; } = "";

        [DataMember(Name = "npcId", Order = 1)]
        public long NpcId { get; set; }

        [DataMember(Name = "zone", Order = 2)]
        public string? Zone { get; set; }

        [DataMember(Name = "content", Order = 3)]
        public string Content { get; set; } = ContentKind.Unknown;

        // Raids name a target in upper case. Groups have none.
        [DataMember(Name = "target", Order = 4)]
        public string? Target { get; set; }

        [DataMember(Name = "readiedAt", Order = 5)]
        public long ReadiedAt { get; set; }

        // Release or cancel time, if either happened.
        [DataMember(Name = "resolvedAt", Order = 6)]
        public long? ResolvedAt { get; set; }

        public BarrageOutcome Outcome { get; set; } = BarrageOutcome.Unresolved;

        [DataMember(Name = "outcome", Order = 7)]
        public string OutcomeName
        {
            get => Outcome.ToString();
            private set { }
        }

        // Bulwarks are only logged for the logger's own group, so a raid barrage on another group can be
        // blocked with no bulwark seen here.
        [DataMember(Name = "bulwarks", Order = 8)]
        public List<BulwarkApply> Bulwarks { get; set; } = new List<BulwarkApply>();

        [DataMember(Name = "damageAbility", Order = 9)]
        public string? DamageAbility { get; set; }

        [DataMember(Name = "targetHits", Order = 10)]
        public int TargetHits { get; set; }

        [DataMember(Name = "targetDamage", Order = 11)]
        public double TargetDamage { get; set; }

        [DataMember(Name = "splashHits", Order = 12)]
        public int SplashHits { get; set; }

        [DataMember(Name = "splashPlayers", Order = 13)]
        public int SplashPlayers { get; set; }

        [DataMember(Name = "splashDamage", Order = 14)]
        public double SplashDamage { get; set; }

        // The emote, release, bulwark and barrage damage lines behind this cast, in log order.
        [DataMember(Name = "lines", Order = 15)]
        public List<SourceLine> Lines { get; set; } = new List<SourceLine>();

        internal HashSet<string> SplashNames { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // False when the release came with no warning emote, so ReadiedAt is only the release time.
        [DataMember(Name = "warningSeen", Order = 16)]
        public bool WarningSeen { get; set; } = true;

        public long? LeadSeconds => WarningSeen ? ResolvedAt - ReadiedAt : null;

        // Seconds from the latest bulwark at or before the release, or null when none was seen in time.
        // Bulwarks applied just after the release are kept in Bulwarks too, so late presses show up.
        public long? BulwarkLeadSeconds
        {
            get
            {
                if (ResolvedAt == null)
                {
                    return null;
                }

                long? lead = null;
                foreach (var bulwark in Bulwarks)
                {
                    if (bulwark.At <= ResolvedAt.Value)
                    {
                        lead = ResolvedAt.Value - bulwark.At;
                    }
                }

                return lead;
            }
        }
    }

    [DataContract]
    public sealed class BulwarkApply
    {
        [DataMember(Name = "by", Order = 0)]
        public string By { get; set; } = "";

        [DataMember(Name = "at", Order = 1)]
        public long At { get; set; }
    }

    [DataContract]
    public sealed class SourceLine
    {
        [DataMember(Name = "at", Order = 0)]
        public long At { get; set; }

        [DataMember(Name = "text", Order = 1)]
        public string Text { get; set; } = "";
    }

    public static class ContentKind
    {
        public const string Raid = "raid";
        public const string Group = "group";
        public const string Unknown = "unknown";

        // Zone names end in a tag: "[Raid]", "[Heroic]" or "[Untold Heroic]".
        public static string FromZone(string? zone)
        {
            if (zone == null)
            {
                return Unknown;
            }

            if (zone.IndexOf("[Raid]", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Raid;
            }

            return zone.IndexOf("Heroic]", System.StringComparison.OrdinalIgnoreCase) >= 0 ? Group : Unknown;
        }
    }
}

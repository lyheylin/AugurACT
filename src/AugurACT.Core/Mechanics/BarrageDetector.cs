using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AugurACT.Core.Eq2;

namespace AugurACT.Core.Mechanics
{
    // Finds the barrage / bulwark mechanic. Feed lines in log order, then call Complete().
    //
    //   \aNPC 1 Boss:Boss\/a readies a barrage!
    //   \#FF00FFBoss prepares to unleash a barrage of attacks toward PLAYER!      (raid; groups omit "toward")
    //   Fighter applies a bulwark to the group.
    //   \aNPC 1 Boss:Boss\/a releases a barrage of attacks at PLAYER!              (raid; groups omit "at")
    //   Boss's Barrage hits Player for ...                                         (raid ability name)
    //   Boss's Barrage: Unfathomable Foray hits Player for ...                     (group ability name)
    //   \#FA58ACBoss cancels the barrage, preferring to perform another task!
    //
    // In raids the barrage always hits its target, and a miss splashes the target's group. In groups a
    // blocked barrage produces no damage lines at all. Either way, damage on anyone but the target means
    // the target's group had no bulwark up. See docs/MECHANICS.md.
    public sealed class BarrageDetector
    {
        public const int HitWindowSeconds = 6;
        public const int MaxLeadSeconds = 20;
        public const int BulwarkLookbackSeconds = 12;
        public const int LateBulwarkSeconds = 4;
        private const int MaxDamageLinesKept = 40;
        private const string BulwarkSuffix = " applies a bulwark to the group.";

        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private static readonly Regex Released = new Regex(@"^releases a barrage of attacks(?: at (?<target>.+?))?!$", Options);
        private static readonly Regex Prepares = new Regex(
            @"^\\#[0-9A-Fa-f]{6}(?<npc>.+?) prepares to unleash a barrage of attacks(?: toward (?<target>.+?))?!$", Options);
        private static readonly Regex Cancels = new Regex(
            @"^\\#[0-9A-Fa-f]{6}(?<npc>.+?) cancels the barrage", Options);
        private static readonly Regex Bulwark = new Regex(@"^(?<by>.+?) applies a bulwark to the group\.$", Options);
        private static readonly Regex Zone = new Regex(@"^You have entered (?::.+?:)?(?<zone>.+)\.$", Options);
        private static readonly Regex BarrageHit = new Regex(
            @"^(?:'s|') (?<ability>Barrage(?:: [^ ].*?)?) (?:hits|multi attacks|flurries|double attacks) ", Options);

        private readonly string? selfName;
        private readonly List<BarrageCast> done = new List<BarrageCast>();
        private readonly Dictionary<string, BarrageCast> readied = new Dictionary<string, BarrageCast>(StringComparer.Ordinal);
        private readonly List<BarrageCast> released = new List<BarrageCast>();
        private readonly Queue<BulwarkApply> recentBulwarks = new Queue<BulwarkApply>();
        private string? zone;
        private long now;

        // selfName is the logging character, whose own lines say "YOU". Without it, a barrage aimed at the
        // logger counts the logger's own hits as splash.
        public BarrageDetector(string? selfName = null, string? zone = null)
        {
            this.selfName = selfName;
            this.zone = zone;
        }

        // A cheap check on a raw log line. Lines that fail it can be skipped without changing the result.
        public static bool MightMatter(string rawLine) =>
            rawLine.IndexOf("arrage", StringComparison.Ordinal) >= 0
            || rawLine.IndexOf(" bulwark ", StringComparison.Ordinal) >= 0
            || rawLine.IndexOf("] You have entered ", StringComparison.Ordinal) >= 0;

        public void Feed(Eq2LogLine line)
        {
            now = line.EpochSeconds;
            ExpireBefore(now);

            var body = line.Body;
            if (body.IndexOf("arrage", StringComparison.Ordinal) >= 0)
            {
                FeedBarrageLine(line, body);
                return;
            }

            if (body.EndsWith(BulwarkSuffix, StringComparison.Ordinal))
            {
                var match = Bulwark.Match(body);
                if (match.Success)
                {
                    AddBulwark(new BulwarkApply { By = match.Groups["by"].Value, At = now }, line);
                }

                return;
            }

            if (body.StartsWith("You have entered ", StringComparison.Ordinal))
            {
                var match = Zone.Match(body);
                if (match.Success)
                {
                    zone = match.Groups["zone"].Value;
                }
            }
        }

        public IReadOnlyList<BarrageCast> Complete()
        {
            ExpireBefore(long.MaxValue);
            done.Sort((a, b) => a.ReadiedAt.CompareTo(b.ReadiedAt));
            return done;
        }

        private void FeedBarrageLine(Eq2LogLine line, string body)
        {
            if (LogMarkup.TryParseLeadingNpc(body, out var npcId, out var npc, out var rest))
            {
                if (rest == "readies a barrage!")
                {
                    if (readied.TryGetValue(npc, out var stale))
                    {
                        Finish(stale);
                    }

                    var cast = new BarrageCast
                    {
                        Npc = npc,
                        NpcId = npcId,
                        Zone = zone,
                        Content = ContentKind.FromZone(zone),
                        ReadiedAt = now,
                    };
                    Keep(cast, line);
                    readied[npc] = cast;
                    return;
                }

                var release = Released.Match(rest);
                if (release.Success)
                {
                    var cast = TakeReadied(npc) ?? new BarrageCast
                    {
                        Npc = npc, Zone = zone, Content = ContentKind.FromZone(zone), ReadiedAt = now, WarningSeen = false,
                    };
                    cast.NpcId = npcId;
                    cast.ResolvedAt = now;
                    cast.Outcome = BarrageOutcome.Blocked;
                    if (release.Groups["target"].Success)
                    {
                        cast.Target = release.Groups["target"].Value;
                    }

                    foreach (var bulwark in recentBulwarks)
                    {
                        if (bulwark.At >= now - BulwarkLookbackSeconds && bulwark.At >= cast.ReadiedAt - 2)
                        {
                            cast.Bulwarks.Add(bulwark);
                            cast.Lines.Add(new SourceLine { At = bulwark.At, Text = bulwark.By + BulwarkSuffix });
                        }
                    }

                    Keep(cast, line);
                    released.Add(cast);
                }

                return;
            }

            var prepare = Prepares.Match(body);
            if (prepare.Success)
            {
                // Usually follows "readies" in the same second, but sometimes arrives alone.
                var name = prepare.Groups["npc"].Value;
                if (!readied.TryGetValue(name, out var cast))
                {
                    cast = new BarrageCast { Npc = name, Zone = zone, Content = ContentKind.FromZone(zone), ReadiedAt = now };
                    readied[name] = cast;
                }

                if (prepare.Groups["target"].Success)
                {
                    cast.Target = prepare.Groups["target"].Value;
                }

                Keep(cast, line);
                return;
            }

            var cancel = Cancels.Match(body);
            if (cancel.Success)
            {
                var cast = TakeReadied(cancel.Groups["npc"].Value);
                if (cast != null)
                {
                    cast.ResolvedAt = now;
                    cast.Outcome = BarrageOutcome.Cancelled;
                    Keep(cast, line);
                    Done(cast);
                }

                return;
            }

            foreach (var cast in released)
            {
                // Callers may skip unrelated lines, so expiry can lag; check the window directly too.
                if (now <= cast.ResolvedAt + HitWindowSeconds
                    && body.StartsWith(cast.Npc, StringComparison.Ordinal)
                    && TryAddHit(cast, line, body.Substring(cast.Npc.Length)))
                {
                    return;
                }
            }
        }

        private bool TryAddHit(BarrageCast cast, Eq2LogLine line, string afterName)
        {
            var match = BarrageHit.Match(afterName);
            if (!match.Success)
            {
                return false;
            }

            var hit = LogEventParser.Parse(line);
            if (hit.Kind != LogEventKind.Damage || hit.Target == null)
            {
                return false;
            }

            cast.DamageAbility ??= match.Groups["ability"].Value;
            var amount = hit.Amount ?? 0;
            if (IsTarget(cast.Target, hit.Target))
            {
                cast.TargetHits++;
                cast.TargetDamage += amount;
            }
            else
            {
                cast.SplashHits++;
                cast.SplashDamage += amount;
                cast.Outcome = BarrageOutcome.Missed;
                cast.SplashNames.Add(hit.Target);
                cast.SplashPlayers = cast.SplashNames.Count;
            }

            if (cast.Lines.Count < MaxDamageLinesKept)
            {
                Keep(cast, line);
            }

            return true;
        }

        private bool IsTarget(string? target, string hitTarget)
        {
            if (target == null)
            {
                return false;
            }

            if (string.Equals(target, hitTarget, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var isSelf = string.Equals(hitTarget, "YOU", StringComparison.Ordinal);
            return isSelf && (target == "YOU" || (selfName != null && string.Equals(target, selfName, StringComparison.OrdinalIgnoreCase)));
        }

        private void AddBulwark(BulwarkApply bulwark, Eq2LogLine line)
        {
            recentBulwarks.Enqueue(bulwark);

            // A press just after the release is still worth showing: it explains a miss as "late".
            foreach (var cast in released)
            {
                if (cast.ResolvedAt != null && bulwark.At - cast.ResolvedAt.Value <= LateBulwarkSeconds)
                {
                    cast.Bulwarks.Add(bulwark);
                    Keep(cast, line);
                }
            }
        }

        private BarrageCast? TakeReadied(string npc)
        {
            if (!readied.TryGetValue(npc, out var cast))
            {
                return null;
            }

            readied.Remove(npc);
            return cast;
        }

        private void ExpireBefore(long time)
        {
            while (recentBulwarks.Count > 0 && recentBulwarks.Peek().At < time - BulwarkLookbackSeconds - 2)
            {
                recentBulwarks.Dequeue();
            }

            for (var i = released.Count - 1; i >= 0; i--)
            {
                var cast = released[i];
                if (time > cast.ResolvedAt + Math.Max(HitWindowSeconds, LateBulwarkSeconds))
                {
                    released.RemoveAt(i);
                    Done(cast);
                }
            }

            if (readied.Count == 0)
            {
                return;
            }

            List<string>? stale = null;
            foreach (var pair in readied)
            {
                if (time > pair.Value.ReadiedAt + MaxLeadSeconds)
                {
                    (stale ??= new List<string>()).Add(pair.Key);
                }
            }

            if (stale != null)
            {
                foreach (var npc in stale)
                {
                    Finish(readied[npc]);
                }
            }
        }

        private void Finish(BarrageCast cast)
        {
            readied.Remove(cast.Npc);
            cast.Outcome = BarrageOutcome.Unresolved;
            Done(cast);
        }

        private void Done(BarrageCast cast)
        {
            cast.SplashNames.Clear();
            cast.Lines.Sort((a, b) => a.At.CompareTo(b.At));
            done.Add(cast);
        }

        private static void Keep(BarrageCast cast, Eq2LogLine line) =>
            cast.Lines.Add(new SourceLine { At = line.EpochSeconds, Text = LogMarkup.Strip(line.Body) });
    }
}

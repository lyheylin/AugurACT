using System;
using System.Text.RegularExpressions;

namespace AugurACT.Core.Eq2
{
    // Patterns follow ACT_English_Parser.cs so both agree on what a combat line is.
    public static class LogEventParser
    {
        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private const string Number = @"[\d,\.KMBTQ]+";
        private const string Critical = @"(?<crit>a .*?critical of )?";
        private const string HitVerb = @"(?:critically )?(?:hits?|flurry|flurries|multi attacks?|double attacks?|aoe attacks?|bash(?:es)?)";

        private static readonly Regex DamageActive = new Regex(
            $@"^(?<subject>.+?) {HitVerb} (?<target>.+?) for {Critical}(?<amounts>.+?) damage\.", Options);
        private static readonly Regex DamagePassive = new Regex(
            $@"^(?<target>.+?) (?:is|are) (?:critically )?(?:hit|multi attack(?:ed)?) by (?<subject>.+?) for {Critical}(?<amounts>.+?) damage\.", Options);
        private static readonly Regex DamageNone = new Regex(
            $@"^(?<subject>.+?) {HitVerb} (?<target>.+?) but fails? to infl[ie]ct any damage\.", Options);
        private static readonly Regex DamagePart = new Regex($@"(?<amount>{Number}) (?<type>[a-z]+)", Options);
        private static readonly Regex Heal = new Regex(
            $@"^(?<subject>.+?) (?:critically )?heals (?<target>.+?) for {Critical}(?<amount>{Number}) hit points?\.", Options);
        private static readonly Regex Refresh = new Regex(
            $@"^(?<subject>.+?) (?:critically )?refreshes (?<target>.+?) for {Critical}(?<amount>{Number}) mana points?\.", Options);
        private static readonly Regex Drain = new Regex(
            $@"^(?<subject>.+?) [a-z]+ (?<target>.+?) draining (?<amount>{Number}) points? of power\.", Options);
        private static readonly Regex Ward = new Regex(
            $@"^(?<subject>.+?) absorbs (?<amount>{Number}) points? of damage from being done to (?<target>.+?)(?: with {Number} points? of damage bleeding through)?\.", Options);
        private static readonly Regex Reduction = new Regex(
            $@"^(?<source>.+?) reduces? the damage from (?<attacker>.+?) to (?<target>.+) by (?<amount>{Number})\.", Options);
        private static readonly Regex Threat = new Regex(
            $@"^(?<subject>.+?) (?:critically )?(?:increases|reduces) .+? hate (?:position )?with (?<target>.+?) (?:by |for )?{Critical}(?<amount>{Number}) (?:threat|positions?)\.", Options);
        private static readonly Regex Cure = new Regex(
            @"^(?<subject>.+?) (?:dispels?|relieves?) (?:(?<target>YOU) of (?<effect>.+?)|(?<effect>.+?) from (?<target>.+))\.", Options);
        private static readonly Regex Avoid = new Regex(
            @"^(?<source>.+?) (?:try|tries) to (?:[^ ]+) (?<target>.+?), but (?<why>.+)\.", Options);
        private static readonly Regex Kill = new Regex(
            @"^(?<source>.+?) (?:has|have) killed (?<target>.+)\.", Options);
        private static readonly Regex Interrupted = new Regex(
            @"^(?<target>.+?) (?:was|were) interrupted!", Options);
        private static readonly Regex Zone = new Regex(
            @"^You have entered (?::.+?:)?(?<zone>.+)\.", Options);
        private static readonly Regex NpcSays = new Regex(
            @"^(?:says|shouts|yells|whispers|tells [^,]+),? ?""?(?<text>.*?)""?$", Options);
        private static readonly Regex Possessive = new Regex(
            @"^(?<owner>.+?)['’]s? (?<ability>.+)$", Options);

        public static LogEvent Parse(Eq2LogLine line)
        {
            var body = line.Body;
            var parsed = new LogEvent { EpochSeconds = line.EpochSeconds };

            if (LogMarkup.TryParseLeadingNpc(body, out var npcId, out var npcName, out var rest)
                && !IsAbilityOfNpc(rest))
            {
                parsed.NpcId = npcId;
                parsed.Source = npcName;
                var says = NpcSays.Match(rest);
                parsed.Kind = says.Success ? LogEventKind.NpcSpeech : LogEventKind.NpcEmote;
                parsed.Text = says.Success ? says.Groups["text"].Value : rest;
                return parsed;
            }

            var isPlayerChat = body.StartsWith(@"\aPC ", StringComparison.Ordinal)
                && body.IndexOf(" tells ", StringComparison.Ordinal) > 0;
            var text = LogMarkup.Strip(body);
            parsed.Text = text;

            if (isPlayerChat || text.StartsWith("You say,", StringComparison.Ordinal) || text.StartsWith("You tell ", StringComparison.Ordinal))
            {
                parsed.Kind = LogEventKind.Chat;
                return parsed;
            }

            if (Contains(text, " damage"))
            {
                if (TryDamage(text, parsed) || TryWard(text, parsed) || TryReduction(text, parsed))
                {
                    return parsed;
                }
            }

            if (Contains(text, " hit point") && TryAmount(Heal, text, LogEventKind.Heal, parsed))
            {
                return parsed;
            }

            if (Contains(text, " hate ") && TryAmount(Threat, text, LogEventKind.Threat, parsed))
            {
                return parsed;
            }

            if (Contains(text, " draining ") && TryAmount(Drain, text, LogEventKind.PowerDrain, parsed))
            {
                return parsed;
            }

            if (Contains(text, " mana point") && TryAmount(Refresh, text, LogEventKind.PowerRefresh, parsed))
            {
                return parsed;
            }

            if ((Contains(text, " relieve") || Contains(text, " dispel")) && TryCure(text, parsed))
            {
                return parsed;
            }

            if (Contains(text, ", but ") && TryAvoid(text, parsed))
            {
                return parsed;
            }

            if (Contains(text, " killed ") && TryKill(text, parsed))
            {
                return parsed;
            }

            if (Contains(text, " interrupted!"))
            {
                var match = Interrupted.Match(text);
                if (match.Success)
                {
                    parsed.Kind = LogEventKind.Interrupt;
                    parsed.Target = match.Groups["target"].Value;
                    return parsed;
                }
            }

            if (text.StartsWith("You have entered ", StringComparison.Ordinal))
            {
                var match = Zone.Match(text);
                if (match.Success)
                {
                    parsed.Kind = LogEventKind.Zone;
                    parsed.Target = match.Groups["zone"].Value;
                    return parsed;
                }
            }

            return parsed;
        }

        private static bool Contains(string text, string value) => text.IndexOf(value, StringComparison.Ordinal) >= 0;

        // A linked NPC name can begin an ordinary combat line rather than speech or an emote.
        private static bool IsAbilityOfNpc(string rest) => rest.StartsWith("'s ", StringComparison.Ordinal);

        private static bool TryDamage(string text, LogEvent parsed)
        {
            var match = DamageActive.Match(text);
            if (!match.Success)
            {
                match = DamagePassive.Match(text);
            }

            if (match.Success)
            {
                parsed.Kind = LogEventKind.Damage;
                SetSubject(match.Groups["subject"].Value, parsed);
                parsed.Target = match.Groups["target"].Value;
                parsed.IsCritical = match.Groups["crit"].Success;
                SetDamageAmounts(match.Groups["amounts"].Value, parsed);
                return true;
            }

            match = DamageNone.Match(text);
            if (!match.Success)
            {
                return false;
            }

            parsed.Kind = LogEventKind.Damage;
            SetSubject(match.Groups["subject"].Value, parsed);
            parsed.Target = match.Groups["target"].Value;
            parsed.Amount = 0;
            return true;
        }

        private static void SetDamageAmounts(string amounts, LogEvent parsed)
        {
            var total = 0d;
            foreach (Match part in DamagePart.Matches(amounts))
            {
                if (ScaledNumber.TryParse(part.Groups["amount"].Value, out var value))
                {
                    total += value;
                    parsed.DamageType ??= part.Groups["type"].Value;
                }
            }

            parsed.Amount = total;
        }

        private static bool TryWard(string text, LogEvent parsed) => TryAmount(Ward, text, LogEventKind.Ward, parsed);

        private static bool TryReduction(string text, LogEvent parsed)
        {
            var match = Reduction.Match(text);
            if (!match.Success)
            {
                return false;
            }

            parsed.Kind = LogEventKind.DamageReduction;
            parsed.Source = match.Groups["source"].Value;
            parsed.Ability = match.Groups["attacker"].Value;
            parsed.Target = match.Groups["target"].Value;
            SetAmount(match.Groups["amount"].Value, parsed);
            return true;
        }

        private static bool TryAmount(Regex rule, string text, LogEventKind kind, LogEvent parsed)
        {
            var match = rule.Match(text);
            if (!match.Success)
            {
                return false;
            }

            parsed.Kind = kind;
            SetSubject(match.Groups["subject"].Value, parsed);
            parsed.Target = match.Groups["target"].Value;
            parsed.IsCritical = match.Groups["crit"].Success;
            SetAmount(match.Groups["amount"].Value, parsed);
            return true;
        }

        private static bool TryCure(string text, LogEvent parsed)
        {
            var match = Cure.Match(text);
            if (!match.Success)
            {
                return false;
            }

            parsed.Kind = LogEventKind.Cure;
            SetSubject(match.Groups["subject"].Value, parsed);
            parsed.Target = match.Groups["target"].Value;
            return true;
        }

        private static bool TryAvoid(string text, LogEvent parsed)
        {
            var match = Avoid.Match(text);
            if (!match.Success)
            {
                return false;
            }

            parsed.Kind = LogEventKind.Avoid;
            parsed.Source = match.Groups["source"].Value;
            parsed.Target = match.Groups["target"].Value;
            return true;
        }

        private static bool TryKill(string text, LogEvent parsed)
        {
            var match = Kill.Match(text);
            if (!match.Success)
            {
                return false;
            }

            parsed.Kind = LogEventKind.Kill;
            parsed.Source = match.Groups["source"].Value;
            parsed.Target = match.Groups["target"].Value;
            return true;
        }

        private static void SetAmount(string amount, LogEvent parsed)
        {
            if (ScaledNumber.TryParse(amount, out var value))
            {
                parsed.Amount = value;
            }
        }

        // Splits on the first possessive, so an NPC whose own name has an apostrophe splits wrongly.
        private static void SetSubject(string subject, LogEvent parsed)
        {
            if (subject.StartsWith("YOUR ", StringComparison.Ordinal))
            {
                parsed.Source = "YOU";
                parsed.Ability = subject.Substring(5);
                return;
            }

            var match = Possessive.Match(subject);
            if (match.Success)
            {
                parsed.Source = match.Groups["owner"].Value;
                parsed.Ability = match.Groups["ability"].Value;
                return;
            }

            parsed.Source = subject;
        }
    }
}

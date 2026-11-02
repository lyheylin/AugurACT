namespace AugurACT.Core.Eq2
{
    public sealed class LogEvent
    {
        public LogEventKind Kind { get; set; }

        public long EpochSeconds { get; set; }

        // "YOU" when the logging character is the source.
        public string? Source { get; set; }

        public string? Ability { get; set; }

        public string? Target { get; set; }

        public double? Amount { get; set; }

        public string? DamageType { get; set; }

        public bool IsCritical { get; set; }

        public long? NpcId { get; set; }

        // Body with link markup removed. For NPC speech and emotes, only what the NPC said or did.
        public string Text { get; set; } = "";
    }
}

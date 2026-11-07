using System.Collections.Generic;
using AugurACT.Core.EventTexts;

namespace AugurACT.Core.Mechanics
{
    // Every mechanic the plugin and the CLI timeline look for. Adding a mechanic means adding its detector here
    // and a list for its results on EncounterSummary; see docs/ADDING-A-MECHANIC.md.
    public static class MechanicSet
    {
        public static IReadOnlyList<IMechanicDetector> Create(ScanContext context) => new IMechanicDetector[]
        {
            new BarrageDetector(context.SelfName, context.Zone),
            new DeathDetector(context),

            // After DeathDetector, which adds the players it finds; event texts read the player list.
            new EventTextDetector(context),
        };
    }
}

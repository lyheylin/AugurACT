using AugurACT.Core.Encounters;
using AugurACT.Core.Eq2;
using AugurACT.Core.Host;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Tests;

public class EncounterStoreTests
{
    private static readonly DateTime Day1 = new(2026, 11, 1);
    private static readonly DateTime Day2 = new(2026, 11, 3);
    private static readonly BarrageCast[] NoCasts = Array.Empty<BarrageCast>();

    private static BarrageCast Cast(string npc, BarrageOutcome outcome) =>
        new() { Npc = npc, Outcome = outcome, ReadiedAt = 1, ResolvedAt = 11 };

    [Fact]
    public void ChosenNpcsAreBossesAndTheRestAreAdds()
    {
        var store = new EncounterStore();
        var result = store.Update("Zone [Raid]", new[] { "Boss" }, new[] { "Boss", "an add", "another add" },
            new[] { Cast("Boss", BarrageOutcome.Missed) }, Day1);

        var encounter = Assert.Single(store.Encounters);
        Assert.Equal("Boss", encounter.Name);
        Assert.Equal(new[] { "Boss" }, encounter.Bosses.Select(b => b.Name));
        Assert.Equal(new[] { "an add", "another add" }, encounter.Adds.Select(a => a.Name));
        Assert.True(encounter.Bosses[0].ReleasesBarrage);
        Assert.False(encounter.Adds[0].ReleasesBarrage);
        Assert.True(result.IsNew);
        Assert.Equal(2, result.Adds);
        Assert.Equal(2, result.AddsAdded);
    }

    [Fact]
    public void ZoneAndBossSetIdentifyTheEncounterInAnyOrderOrCase()
    {
        var store = new EncounterStore();
        store.Update("Zone", new[] { "Gawk Oglth", "Gaze Oglth" }, new[] { "add one" }, NoCasts, Day1);
        var again = store.Update("zone", new[] { "gaze oglth", "Gawk Oglth" }, new[] { "add two" }, NoCasts, Day2);
        store.Update("Zone", new[] { "Gawk Oglth" }, NoCasts.Select(c => c.Npc), NoCasts, Day2);
        store.Update("Other Zone", new[] { "Gawk Oglth", "Gaze Oglth" }, NoCasts.Select(c => c.Npc), NoCasts, Day2);

        Assert.False(again.IsNew);
        Assert.Equal(1, again.AddsAdded);
        Assert.Equal(3, store.Encounters.Count);
        var pair = store.Find("ZONE", new[] { "Gaze Oglth", "Gawk Oglth" })!;
        Assert.Equal(2, pair.Updates);
        Assert.Equal("2026-11-01", pair.FirstRecorded);
        Assert.Equal("2026-11-03", pair.Updated);
        Assert.Equal(new[] { "add one", "add two" }, pair.Adds.Select(a => a.Name));
    }

    [Fact]
    public void BarrageStaysYesWhenALaterFightHasNoRelease()
    {
        var store = new EncounterStore();
        store.Update("Zone", new[] { "Boss" }, new[] { "add" }, new[] { Cast("Boss", BarrageOutcome.Blocked), Cast("add", BarrageOutcome.Missed) }, Day1);
        store.Update("Zone", new[] { "Boss" }, new[] { "add" }, NoCasts, Day2);

        var encounter = store.Find("Zone", new[] { "Boss" })!;
        Assert.True(encounter.Bosses[0].ReleasesBarrage);
        Assert.True(encounter.Adds[0].ReleasesBarrage);
    }

    [Theory]
    [InlineData(BarrageOutcome.Cancelled)]
    [InlineData(BarrageOutcome.Unresolved)]
    public void WarningWithoutReleaseDoesNotCount(BarrageOutcome outcome)
    {
        var store = new EncounterStore();
        store.Update("Zone", new[] { "Boss" }, NoCasts.Select(c => c.Npc), new[] { Cast("Boss", outcome) }, Day1);

        Assert.False(store.Encounters[0].Bosses[0].ReleasesBarrage);
    }

    [Fact]
    public void SortsByZoneThenEncounterAndNeedsABoss()
    {
        var store = new EncounterStore();
        store.Update("b zone", new[] { "Zed" }, new[] { "z add", "a add" }, NoCasts, Day1);
        store.Update("B Zone", new[] { "Alpha" }, NoCasts.Select(c => c.Npc), NoCasts, Day1);
        store.Update("", new[] { "Mystery" }, NoCasts.Select(c => c.Npc), NoCasts, Day1);
        store.Update("A Zone", new[] { "Boss" }, NoCasts.Select(c => c.Npc), NoCasts, Day1);

        Assert.Equal(new[] { "(unknown zone)", "A Zone", "B Zone", "b zone" }, store.Encounters.Select(e => e.Zone));
        Assert.Equal(new[] { "Mystery", "Boss", "Alpha", "Zed" }, store.Encounters.Select(e => e.Name));
        Assert.Equal(new[] { "a add", "z add" }, store.Encounters[3].Adds.Select(a => a.Name));
        Assert.Throws<ArgumentException>(() => store.Update("Zone", new[] { " " }, new[] { "x" }, NoCasts, Day1));
    }

    [Fact]
    public void SavesAndLoads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "augur-enc-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "Config", "encounters.json");
        try
        {
            var store = new EncounterStore();
            store.Update("Zone: Part/Two [Raid]", new[] { "Boss", "Second Boss" }, new[] { "Add" }, new[] { Cast("Boss", BarrageOutcome.Blocked) }, Day1);
            store.Save(path);
            store.Update("Zone: Part/Two [Raid]", new[] { "Boss", "Second Boss" }, new[] { "Other Add" }, NoCasts, Day2);
            store.Save(path);

            var text = File.ReadAllText(path);
            Assert.Contains("Part/Two", text);
            Assert.Contains("\"name\": \"Boss, Second Boss\"", text);
            var loaded = EncounterStore.Load(path).Find("Zone: Part/Two [Raid]", new[] { "Second Boss", "Boss" })!;
            Assert.True(loaded.Bosses[0].ReleasesBarrage);
            Assert.False(loaded.Bosses[1].ReleasesBarrage);
            Assert.Equal(2, loaded.Adds.Count);
            Assert.Equal(2, loaded.Updates);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

public class HostMessageTests
{
    [Fact]
    public void ParsesPageRequestsAndIgnoresJunk()
    {
        var request = HostMessages.ParseRequest("{\"type\":\"update-encounter\",\"id\":\"1a-2b-3\",\"bosses\":[\"A\",\"B\"],\"extra\":1}");
        Assert.Equal(HostMessages.UpdateEncounter, request!.Type);
        Assert.Equal("1a-2b-3", request.Id);
        Assert.Equal(new[] { "A", "B" }, request.Bosses);

        Assert.Null(HostMessages.ParseRequest("{\"type\":\"list\"}")!.Bosses);
        Assert.Null(HostMessages.ParseRequest("not json"));
        Assert.Null(HostMessages.ParseRequest("\"just a string\"")?.Type);
    }

    [Fact]
    public void NpcListMergesEnemiesAndBarrageNpcsByDamageTaken()
    {
        var npcs = EncounterNpcsMessage.Collect(
            new[]
            {
                new EncounterNpc { Name = "an add", DamageTaken = 5 },
                new EncounterNpc { Name = "Boss", DamageTaken = 900 },
                new EncounterNpc { Name = "AN ADD", DamageTaken = 10 },
                new EncounterNpc { Name = " ", DamageTaken = 1 },
            },
            new[] { new BarrageCast { Npc = "Boss" }, new BarrageCast { Npc = "Hidden Caster" } });

        Assert.Equal(new[] { "Boss", "an add", "Hidden Caster" }, npcs.Select(n => n.Name));
        Assert.Equal(15, npcs[1].DamageTaken);
    }

    [Fact]
    public void AnalysisMessageCarriesTheSummary()
    {
        var summary = EncounterSummary.Build("Boss", "Zone [Heroic]", new[]
        {
            "(1791593375)[Sat Oct 10 08:49:35 2026] \\aNPC 12 Boss:Boss\\/a readies a barrage!",
            "(1791593380)[Sat Oct 10 08:49:40 2026] \\aNPC 12 Boss:Boss\\/a releases a barrage of attacks!",
            "(1791593400)[Sat Oct 10 08:50:00 2026] Boss hits YOU for 5 crushing damage.",
        });
        var json = HostMessages.ToJson(new AnalysisMessage { Id = "x", Summary = summary });

        Assert.StartsWith("{\"type\":\"analysis\",\"id\":\"x\"", json);
        Assert.Contains("\"outcome\":\"Blocked\"", json);
        Assert.DoesNotContain("\"error\"", json);
        Assert.Equal(1791593400, summary.EndEpoch);
        Assert.Equal(3, summary.ParsedLines);
    }

    [Theory]
    [InlineData("(1791593375)[Sat Oct 10 08:49:35 2026] text", true, 1791593375)]
    [InlineData("(1791593375)[Sat Oct 10 08:49:35 2026] ", true, 1791593375)]
    [InlineData("(179159337x)[Sat Oct 10 08:49:35 2026] text", false, 0)]
    [InlineData("(1791593375) no date", false, 0)]
    [InlineData("guild MOTD", false, 0)]
    public void ReadsEpochPrefixLikeTheFullParser(string line, bool ok, long epoch)
    {
        Assert.Equal(ok, Eq2LogLine.TryReadEpoch(line, out var read));
        Assert.Equal(epoch, read);
        Assert.Equal(ok, Eq2LogLine.TryParse(line, out _));
    }
}

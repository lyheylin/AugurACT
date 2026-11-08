using System.Text.RegularExpressions;
using AugurACT.Core.Encounters;
using AugurACT.Core.EventTexts;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Tests;

public class EventTextTests
{
    private const long T0 = 1791593375;
    private static readonly DateTime Day1 = new(2026, 11, 1);

    // Players use named abilities, which is how the roster knows them; Bossy never does.
    private static readonly string[] Players = { "Healerc", "Tanka", "Archerss" };

    private static List<EventTextGroup> Run(IEnumerable<Regex>? blacklist, params string[] bodies) =>
        Run(new ScanOptions { EventTextBlacklist = (blacklist ?? Array.Empty<Regex>()).ToList() }, bodies);

    private static List<EventTextGroup> Run(ScanOptions options, params string[] bodies) =>
        EncounterSummary.Build(
            "Test",
            "Test Zone",
            Players.Select(p => $"({T0 - 1})[Sat Oct 10 08:49:34 2026] {p}'s Strike hits a test add for 1 crushing damage.")
                .Concat(bodies.Select((b, i) => $"({T0 + i})[Sat Oct 10 08:49:35 2026] {b}")),
            "Healerc",
            Players,
            options).EventTexts;

    private static List<EventTextGroup> Run(params string[] bodies) => Run(ScanOptions.None, bodies);

    private static EventTextEdit Entry(string kind, string text, string? description = null) =>
        new() { Kind = kind, Text = text, Description = description };

    private static string Npc(string name, string rest) => $@"\aNPC 4773594 {name}:{name}\/a {rest}";

    [Fact]
    public void KindsComeFromTheLineShape()
    {
        var groups = Run(
            Npc("Bossy", "readies a barrage!"),
            Npc("Bossy", "says, \"Kneel before me!\""),
            @"\#FF00FFThe ground shakes!",
            "Bossy's Smash hits Tanka for 10 crushing damage.");

        Assert.Equal(new[] { "emote", "say", "narration" }, groups.Select(g => g.Kind));
        Assert.Equal(new[] { "Bossy readies a barrage!", "Bossy says, \"Kneel before me!\"", "The ground shakes!" },
            groups.Select(g => g.Template));
        Assert.Equal("Bossy", groups[0].Occurrences[0].Npc);
        Assert.Null(groups[2].Occurrences[0].Npc);
    }

    [Fact]
    public void LinesThatDifferOnlyByPlayerAreOneGroup()
    {
        var groups = Run(
            @"\#FFFF00Bossy invokes divine devastation at Tanka!",
            @"\#FFFF00Bossy invokes divine devastation at ARCHERSS!",
            @"\#FFFF00Bossy invokes divine devastation at YOU!");

        var group = Assert.Single(groups);
        Assert.Equal("Bossy invokes divine devastation at {player}!", group.Template);
        Assert.Equal(3, group.Occurrences.Count);
        var slot = Assert.Single(group.Parts, p => p.Slot != null);
        Assert.Equal(new[] { "Tanka", "ARCHERSS", "YOU" }, slot.Values);
        Assert.True(slot.Variable);

        // Each line names a player lane: capitals become the player's spelling, YOU the logger.
        Assert.Equal(new[] { "Tanka", "Archerss", "Healerc" }, group.Occurrences.Select(o => Assert.Single(o.Players)));
    }

    [Fact]
    public void APlayerSeenOnceIsStillAVariable()
    {
        var group = Assert.Single(Run(Npc("Bossy", "commits Tanka to memory.")));

        Assert.Equal("Bossy commits {player} to memory.", group.Template);
    }

    [Fact]
    public void ClassesAndNumbersBecomeVariablesOnlyWhenTheyVary()
    {
        var groups = Run(
            @"\#FFD700Heroic Surge \#FFFFFFhas been triggered by: \#00CEEBMAGE\#FFFFFF!",
            @"\#FFD700Heroic Surge \#FFFFFFhas been triggered by: \#00CEEBSCOUT\#FFFFFF!",
            @"\#FF0000Bossy grows stronger for 20 seconds!",
            @"\#FF0000Bossy summons 3 adds.",
            @"\#FF0000Bossy summons 4 adds.");

        Assert.Equal(new[]
        {
            "Heroic Surge has been triggered by: {archetype}!",
            "Bossy grows stronger for 20 seconds!",
            "Bossy summons {number} adds.",
        }, groups.Select(g => g.Template));

        var unchanged = Assert.Single(groups[1].Parts, p => p.Slot != null);
        Assert.Equal(EventTextTemplate.Number, unchanged.Slot);
        Assert.False(unchanged.Variable);
        Assert.Equal(new[] { "20" }, unchanged.Values);
    }

    [Fact]
    public void ACharmedPlayerSpeakingAsAnNpcGoesToTheirOwnLane()
    {
        var group = Assert.Single(Run(
            Npc("Tanka", "says, \"Your blood calls to me.\""),
            Npc("Archerss", "says, \"Your blood calls to me.\"")));

        Assert.Equal("{player} says, \"Your blood calls to me.\"", group.Template);
        Assert.Equal(new[] { "Tanka", "Archerss" }, group.Occurrences.Select(o => o.Player));
        Assert.All(group.Occurrences, o => Assert.Null(o.Npc));
    }

    [Fact]
    public void ItemLinksAndPlayerChatAreNotNarration()
    {
        var groups = Run(
            @"Repaired: \aITEM 1726251451 2102428547:\#f0c0a0Ring of Things\/a.",
            @"\aPC -1 Tanka:Tanka\/a says to the raid party, ""\#FF0000look out""",
            @"\aNPC 4773594 Bossy:Bossy\/a's Smash hits Tanka for 10 crushing damage.");

        Assert.Empty(groups);
    }

    [Fact]
    public void BlacklistedTextsAreLeftOut()
    {
        var blacklist = new[] { EventTextTemplate.ToRegex("Heroic Surge has been triggered by: {archetype}!") };
        var groups = Run(blacklist,
            @"\#FFD700Heroic Surge \#FFFFFFhas been triggered by: \#00CEEBMAGE\#FFFFFF!",
            @"\#FFD700Heroic Surge \#FFFFFFhas been triggered by: \#00CEEBFIGHTER\#FFFFFF!",
            @"\#FF00FFThe ground shakes!");

        Assert.Equal("The ground shakes!", Assert.Single(groups).Template);
    }

    [Theory]
    [InlineData("{player} is marked!", "Tanka is marked!", true)]
    [InlineData("{player} is marked!", "TANKA is marked!", true)]
    [InlineData("{player} is marked!", "Tanka is marked! Again!", false)]
    [InlineData("Wave {number} of {number}", "Wave 2 of 10", true)]
    [InlineData("Wave {number} of {number}", "Wave two of 10", false)]
    [InlineData("Called by a {class}.", "Called by a Shadowknight.", true)]
    [InlineData("Called by a {class}.", "Called by a Mage.", false)]
    [InlineData("Called by: {archetype}!", "Called by: FIGHTER, PRIEST!", false)]
    [InlineData("Called by: {archetype}, {archetype}!", "Called by: FIGHTER, PRIEST!", true)]
    [InlineData("A {{brace}} (and dots...)", "A {brace} (and dots...)", true)]
    public void TemplatesBecomeRegexes(string template, string line, bool matches)
    {
        Assert.Equal(matches, EventTextTemplate.ToRegex(template).IsMatch(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{target} is marked")]
    [InlineData("a lone { brace")]
    public void BadTemplatesAreRefused(string template)
    {
        Assert.False(EventTextTemplate.TryParse(template, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void EventTextsAreAddedToARecordedEncounterOnce()
    {
        var store = new EncounterStore();
        store.Update("Zone [Raid]", new[] { "Bossy" }, Array.Empty<string>(), Array.Empty<BarrageCast>(), Day1);

        Assert.True(store.SaveEventText("Zone [Raid]", "Bossy", Entry("emote", "Bossy readies a barrage!"), Day1));
        Assert.False(store.SaveEventText("zone [raid]", "bossy", Entry("emote", "Bossy readies a barrage!"), Day1));
        Assert.Throws<ArgumentException>(() => store.SaveEventText("Zone [Raid]", "Nobody", Entry("emote", "x"), Day1));
        Assert.Throws<ArgumentException>(() => store.SaveEventText("Zone [Raid]", "Bossy", Entry("emote", "{bad}"), Day1));

        var text = Assert.Single(store.Encounters[0].EventTexts);
        Assert.Equal("2026-11-01", text.Added);
        Assert.True(store.RemoveEventText("Zone [Raid]", "Bossy", "Bossy readies a barrage!"));
        Assert.Empty(store.Encounters[0].EventTexts);
    }

    [Fact]
    public void SavingAgainUpdatesTheNoteAndOnlyTouchesTheTimerWhenAsked()
    {
        var store = new EncounterStore();
        store.Update("Zone", new[] { "Bossy" }, Array.Empty<string>(), Array.Empty<BarrageCast>(), Day1);
        var timer = new RecordedTimer { Mean = 60, Spread = 2, Intervals = 3 };
        store.SaveEventText("Zone", "Bossy", new EventTextEdit { Kind = "emote", Text = "Bossy roars!", SetTimer = true, Timer = timer }, Day1);

        Assert.False(store.SaveEventText("Zone", "Bossy", Entry("emote", "Bossy roars!", "  Spread out.  "), Day1));
        var text = Assert.Single(store.Encounters[0].EventTexts);
        Assert.Equal("Spread out.", text.Description);
        Assert.Same(timer, text.Timer);

        store.SaveEventText("Zone", "Bossy", new EventTextEdit { Kind = "emote", Text = "Bossy roars!", SetTimer = true }, Day1);
        Assert.Null(text.Timer);
        Assert.Null(text.Description);

        Assert.Throws<ArgumentException>(() => store.SaveEventText("Zone", "Bossy",
            new EventTextEdit { Kind = "emote", Text = "Bossy roars!", SetTimer = true, Timer = new RecordedTimer() }, Day1));
    }

    [Fact]
    public void EditingAnEntryCanChangeItsText()
    {
        var store = new EncounterStore();
        store.Update("Zone", new[] { "Bossy" }, Array.Empty<string>(), Array.Empty<BarrageCast>(), Day1);
        store.SaveEventText("Zone", "Bossy", Entry("emote", "Bossy roars at Tanka!"), Day1);

        Assert.False(store.SaveEventText("Zone", "Bossy",
            new EventTextEdit { Kind = "emote", Text = "Bossy roars at {player}!", Original = "Bossy roars at Tanka!" }, Day1));
        Assert.Equal("Bossy roars at {player}!", Assert.Single(store.Encounters[0].EventTexts).Text);
    }

    [Fact]
    public void EachArchetypeInAListIsItsOwnVariable()
    {
        var groups = Run(
            @"\#FFD700Heroic Surge \#FFFFFFhas been triggered by: \#00CEEBFIGHTER, PRIEST\#FFFFFF!",
            @"\#FFD700Heroic Surge \#FFFFFFhas been triggered by: \#00CEEBPRIEST, SCOUT\#FFFFFF!",
            @"\#FFD700Heroic Surge \#FFFFFFhas been triggered by: \#00CEEBFIGHTER, PRIEST, SCOUT\#FFFFFF!");

        Assert.Equal(new[]
        {
            "Heroic Surge has been triggered by: {archetype}, {archetype}!",
            "Heroic Surge has been triggered by: FIGHTER, PRIEST, SCOUT!",
        }, groups.Select(g => g.Template));
        Assert.Equal(new[] { "FIGHTER", "PRIEST" }, groups[0].Occurrences[0].Values);
        Assert.All(groups.SelectMany(g => g.Parts).Where(p => p.Values != null).SelectMany(p => p.Values!), v => Assert.DoesNotContain(",", v));
    }

    [Fact]
    public void BlacklistingRemovesTheTextFromEveryEncounter()
    {
        var store = new EncounterStore();
        store.Update("Zone", new[] { "Bossy" }, Array.Empty<string>(), Array.Empty<BarrageCast>(), Day1);
        store.SaveEventText("Zone", "Bossy", Entry("narration", "Heroic Surge has been triggered by: MAGE!"), Day1);

        Assert.True(store.AddToBlacklist("narration", "Heroic Surge has been triggered by: MAGE!", Day1));
        Assert.False(store.AddToBlacklist("narration", "Heroic Surge has been triggered by: MAGE!", Day1));

        Assert.Empty(store.Encounters[0].EventTexts);
        Assert.Matches(Assert.Single(store.BlacklistRules()), "Heroic Surge has been triggered by: MAGE!");
        Assert.True(store.RemoveFromBlacklist("Heroic Surge has been triggered by: MAGE!"));
        Assert.Empty(store.Blacklist);
    }

    [Fact]
    public void EventTextsAndBlacklistSurviveSaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"augur-{Guid.NewGuid():N}.json");
        try
        {
            var store = new EncounterStore();
            store.Update("Zone", new[] { "Bossy" }, Array.Empty<string>(), Array.Empty<BarrageCast>(), Day1);
            store.SaveEventText("Zone", "Bossy", new EventTextEdit
            {
                Kind = "say",
                Text = "Bossy says, \"{player}, you're next!\"",
                Description = "Run to the wall.",
                SetTimer = true,
                Timer = new RecordedTimer { Mean = 219.7, Spread = 13.3, Intervals = 7, Fights = { T0 } },
            }, Day1);
            store.AddToBlacklist("narration", "Time Warp is now ready for use!", Day1);
            store.Save(path);

            var loaded = EncounterStore.Load(path);
            var text = Assert.Single(loaded.Encounters[0].EventTexts);
            Assert.Equal("Bossy says, \"{player}, you're next!\"", text.Text);
            Assert.Equal("Run to the wall.", text.Description);
            Assert.Equal(219.7, text.Timer!.Mean);
            Assert.Equal(new[] { T0 }, text.Timer.Fights);
            Assert.Equal("Time Warp is now ready for use!", Assert.Single(loaded.Blacklist).Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FilesFromBeforeEventTextsStillLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"augur-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"encounters\":[{\"zone\":\"Zone\",\"name\":\"Bossy\",\"bosses\":[{\"name\":\"Bossy\"}],\"adds\":[],\"updates\":1}]}");
            var loaded = EncounterStore.Load(path);
            Assert.Empty(loaded.Blacklist);
            Assert.Empty(loaded.Encounters[0].EventTexts);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class EventTextConfigTests
{
    [Fact]
    public void ThePageGetsEveryVariableFromCore()
    {
        var config = AugurACT.Core.EventTexts.EventTextConfig.Create();

        Assert.Equal(AugurACT.Core.EventTexts.EventTextTemplate.SlotKinds, config.Variables.Select(v => v.Name));
        Assert.Equal(new[] { "player" }, config.Variables.Where(v => v.Always).Select(v => v.Name));
        Assert.Contains("\"type\":\"config\"", AugurACT.Core.Host.HostMessages.ToJson(new AugurACT.Core.Host.PageConfigMessage()));
    }

    [Theory]
    [InlineData("Called by Mage and FURY for 20 seconds", "archetype:Mage class:FURY number:20")]
    [InlineData("Stand 10m away, 1.2B damage", "number:1.2B")]
    [InlineData("Magespell and Furyous", "")]
    public void TheCandidatePatternFindsClassesArchetypesAndNumbers(string text, string expected)
    {
        var rule = new System.Text.RegularExpressions.Regex(AugurACT.Core.EventTexts.EventTextConfig.Create().Candidate);
        var found = rule.Matches(text).Select(m => new[] { "class", "archetype", "number" }.First(g => m.Groups[g].Success) + ":" + m.Value);
        Assert.Equal(expected, string.Join(" ", found));
    }
}

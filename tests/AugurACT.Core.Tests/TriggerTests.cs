using System.Text.RegularExpressions;
using AugurACT.Core.Encounters;
using AugurACT.Core.EventTexts;
using AugurACT.Core.Host;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Tests;

// Trigger regexes from event texts, ACT's XML for them, and the page's trigger edits.
public class TriggerTests
{
    private const string Stamp = "(1794152316)[Sun Nov  8 23:38:36 2026] ";
    private static readonly DateTime Day1 = new(2026, 11, 1);

    private static void MatchesLog(string pattern, params string[] bodies)
    {
        foreach (var body in bodies)
        {
            Assert.Matches(new Regex(pattern), Stamp + body);
        }
    }

    [Fact]
    public void AnEmoteStartsAtTheNpcNameAndKeepsTheLinkEnd()
    {
        const string line = @"\aNPC 7273717 A ghouly bladegaze:A ghouly bladegaze\/a is summoned into the area to combat your scouts!";
        var regex = TriggerRegex.Build("emote", "A ghouly bladegaze is summoned into the area to combat your scouts!", line);

        Assert.True(regex.FromLine);
        Assert.Equal(@"A ghouly bladegaze\\/a is summoned into the area to combat your scouts!", regex.Pattern);
        MatchesLog(regex.Pattern, line, @"\aNPC 7600989 A ghouly bladegaze:A ghouly bladegaze\/a is summoned into the area to combat your scouts!");
    }

    [Fact]
    public void TheXmlIsWhatActSharesWithBackslashesAsCharacterReferences()
    {
        var trigger = new EventTrigger
        {
            Regex = @"A ghouly bladegaze\\/a is summoned into the area to combat your scouts!",
            Sound = EventTrigger.SoundTts,
            SoundData = "Scout add",
            Category = "Zon Zobboz: Revelations of Consequence [Raid]",
            RestrictToZone = true,
        };

        Assert.Equal(
            "<Trigger R=\"A ghouly bladegaze&#92;&#92;/a is summoned into the area to combat your scouts!\" SD=\"Scout add\" ST=\"3\" CR=\"T\" C=\"Zon Zobboz: Revelations of Consequence [Raid]\" T=\"F\" TN=\"\" Ta=\"F\" />",
            trigger.ToXml());
    }

    [Fact]
    public void TheXmlEscapesWhatXmlNeedsAndAnythingOutsideAscii()
    {
        var xml = new EventTrigger { Regex = "a<b>&\"c\" 'd' é", Timer = true, TimerName = "Pox", ResultsTab = true }.ToXml();

        Assert.Contains("R=\"a&lt;b&gt;&amp;&quot;c&quot; &apos;d&apos; &#233;\"", xml);
        Assert.Contains("ST=\"0\" CR=\"F\" C=\"\" T=\"T\" TN=\"Pox\" Ta=\"T\"", xml);
        Assert.Equal("Pox", System.Xml.Linq.XElement.Parse(xml).Attribute("TN")!.Value);
        Assert.Equal("a<b>&\"c\" 'd' é", System.Xml.Linq.XElement.Parse(xml).Attribute("R")!.Value);
    }

    [Fact]
    public void NarrationKeepsItsColourCodesAndPlayersBecomeNamedGroups()
    {
        const string line = @"\#00FF00Thresinet targets Tanka for Flesh Decay!";
        var regex = TriggerRegex.Build("narration", "Thresinet targets {player} for Flesh Decay!", line);

        Assert.Equal(@"\\#00FF00Thresinet targets (?<player>[A-Za-z]+) for Flesh Decay!", regex.Pattern);
        MatchesLog(regex.Pattern, line, @"\#00FF00Thresinet targets Healerc for Flesh Decay!");
        Assert.Equal("Healerc", new Regex(regex.Pattern).Match(@"\#00FF00Thresinet targets Healerc for Flesh Decay!").Groups["player"].Value);
    }

    [Fact]
    public void ColourCodesInTheMiddleStayAndRepeatedVariablesAreNumbered()
    {
        const string line = @"\#FFD700Heroic Surge \#FFFFFFhas been triggered by: \#87CEEBFIGHTER\#FFFFFF, \#87CEEBMAGE\#FFFFFF!";
        var regex = TriggerRegex.Build("narration", "Heroic Surge has been triggered by: {archetype}, {archetype}!", line);

        Assert.True(regex.FromLine);
        Assert.Equal(
            @"\\#FFD700Heroic Surge \\#FFFFFFhas been triggered by: \\#87CEEB(?<archetype1>(?i:Fighter|Priest|Scout|Mage))\\#FFFFFF, \\#87CEEB(?<archetype2>(?i:Fighter|Priest|Scout|Mage))\\#FFFFFF!",
            regex.Pattern);
        MatchesLog(regex.Pattern, line, @"\#FFD700Heroic Surge \#FFFFFFhas been triggered by: \#87CEEBPRIEST\#FFFFFF, \#87CEEBSCOUT\#FFFFFF!");
    }

    [Fact]
    public void ALinkLaterInTheLineKeepsAPatternForItsId()
    {
        const string line = @"\aNPC 4773594 Bossy:Bossy\/a glares at \aPC -1 Tanka:Tanka\/a!";
        var regex = TriggerRegex.Build("emote", "Bossy glares at {player}!", line);

        Assert.Equal(@"Bossy\\/a glares at \\aPC -?\d+ [^\\]*?:(?<player>[A-Za-z]+)\\/a!", regex.Pattern);
        MatchesLog(regex.Pattern, line, @"\aNPC 99 Bossy:Bossy\/a glares at \aPC 12345 Archerss:Archerss\/a!");
    }

    [Fact]
    public void RegexCharactersInTheTextAreEscaped()
    {
        const string line = @"\aNPC 1 Bossy:Bossy\/a says, ""Who (dares) [enter] my lair? $5... +1 {x}|y^""";
        var regex = TriggerRegex.Build("say", "Bossy says, \"Who (dares) [enter] my lair? $5... +1 {{x}}|y^\"", line);

        Assert.True(regex.FromLine);
        Assert.Equal(@"Bossy\\/a says, \""Who \(dares\) \[enter\] my lair\? \$5\.\.\. \+1 \{x\}\|y\^\""", regex.Pattern);
        MatchesLog(regex.Pattern, line);
    }

    [Fact]
    public void QuotesAreEscapedWithoutALineToo()
    {
        var regex = TriggerRegex.Build("say", "Bossy says, \"Kneel* before me!\"", null, new[] { "Bossy" });

        Assert.False(regex.FromLine);
        Assert.Equal(@"Bossy\\/a says, \""Kneel\* before me!\""", regex.Pattern);
        Assert.Matches(regex.Pattern, @"\aNPC 1 Bossy:Bossy\/a says, ""Kneel* before me!""");
    }

    [Fact]
    public void WithoutALineTheLinkEndGoesAfterAListedNpcName()
    {
        var regex = TriggerRegex.Build("emote", "A ghouly bladegaze is summoned!", null, new[] { "A ghouly", "a Ghouly Bladegaze", "Bossy" });

        Assert.False(regex.FromLine);
        Assert.Equal(@"A ghouly bladegaze\\/a is summoned!", regex.Pattern);
    }

    [Fact]
    public void ATextThatNoLongerFitsItsLineIsBuiltFromTheText()
    {
        var regex = TriggerRegex.Build("narration", "Thresinet targets {player} for Void Pox!", @"\#00FF00Thresinet targets Tanka for Flesh Decay!");

        Assert.False(regex.FromLine);
        Assert.Equal("Thresinet targets (?<player>[A-Za-z]+) for Void Pox!", regex.Pattern);
    }

    [Fact]
    public void ASavedLineKeepsNoPlayerNames()
    {
        const string template = "Bossy glares at {player} and {player}, a {class}!";
        const string line = @"\aNPC 12 Bossy:Bossy\/a glares at \aPC 345 Tanka:Tanka\/a and Healerc, a \#FF0000Wizard!";
        var anonymous = TriggerRegex.Anonymize(template, line);

        Assert.Equal(@"\aNPC 12 Bossy:Bossy\/a glares at \aPC 0 Player:Player\/a and Player, a \#FF0000Wizard!", anonymous);
        Assert.Equal(TriggerRegex.Build("emote", template, line).Pattern, TriggerRegex.Build("emote", template, anonymous).Pattern);

        // A line that doesn't fit the text isn't kept, since its names can't be found.
        Assert.Null(TriggerRegex.Anonymize("Bossy roars!", @"Tanka roars!"));
    }

    [Fact]
    public void LinesInTheEncountersDataLoseTheirNamesWhenSavedAndLoaded()
    {
        var store = Recorded();
        Run(store, """
            {"type":"add-event-text","zone":"Zone","name":"Bossy",
             "entry":{"kind":"emote","text":"Bossy glares at {player}!","line":"\\aNPC 12 Bossy:Bossy\\/a glares at \\aPC 345 Tanka:Tanka\\/a!"}}
            """);
        Assert.Equal(@"\aNPC 12 Bossy:Bossy\/a glares at \aPC 0 Player:Player\/a!", store.Encounters[0].EventTexts[0].Line);

        // Data saved before names were taken out.
        var path = Path.Combine(Path.GetTempPath(), $"augur-names-{Guid.NewGuid():N}.json");
        try
        {
            store.Encounters[0].EventTexts[0].Line = @"\aNPC 12 Bossy:Bossy\/a glares at \aPC 345 Tanka:Tanka\/a!";
            store.Save(path);
            Assert.Equal(@"\aNPC 12 Bossy:Bossy\/a glares at \aPC 0 Player:Player\/a!", EncounterStore.Load(path).Encounters[0].EventTexts[0].Line);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RepeatedVariablesAreNumberedFromOne()
    {
        var fromLine = TriggerRegex.Build("narration", "{player} and {player} are linked!", @"\#FF0000Tanka and Healerc are linked!");
        var fromText = TriggerRegex.Build("narration", "{player} and {player} are linked!", null);

        Assert.Equal(@"\\#FF0000(?<player1>[A-Za-z]+) and (?<player2>[A-Za-z]+) are linked!", fromLine.Pattern);
        Assert.Equal("(?<player1>[A-Za-z]+) and (?<player2>[A-Za-z]+) are linked!", fromText.Pattern);
    }

    [Fact]
    public void EveryGroupFromAFightHasARegexMatchingAllItsLines()
    {
        string[] players = { "Healerc", "Tanka", "Archerss" };
        var bodies = new[]
        {
            @"\aNPC 4773594 Bossy:Bossy\/a commits Tanka to memory.",
            @"\aNPC 4773594 Bossy:Bossy\/a commits ARCHERSS to memory.",
            @"\#FFFF00Bossy invokes divine devastation at YOU!",
        };
        var lines = players.Select(p => $"{Stamp}{p}'s Strike hits a test add for 1 crushing damage.").Concat(bodies.Select(b => Stamp + b));
        var groups = EncounterSummary.Build("Test", "Zone", lines, "Healerc", players, ScanOptions.None).EventTexts;

        Assert.Equal(2, groups.Count);
        foreach (var group in groups)
        {
            var regex = new Regex(group.Regex);
            Assert.All(group.Occurrences, o => Assert.Matches(regex, Stamp + o.Raw));
        }

        Assert.Equal(bodies[0], groups[0].Occurrences[0].Raw);
        Assert.Equal(@"Bossy\\/a commits (?<player>[A-Za-z]+) to memory\.", groups[0].Regex);
    }

    // ---- the page's trigger edits ----

    private static StoreEditResult Run(EncounterStore store, string json)
    {
        var type = HostMessages.ParseRequest(json)!.Type;
        Assert.True(StoreEditSet.TryGet(type, out var edit), $"no edit registered for {type}");
        return edit.Apply(json, store, Day1);
    }

    private static EncounterStore Recorded()
    {
        var store = new EncounterStore();
        store.Update("Zone", new[] { "Bossy" }, Array.Empty<string>(), Array.Empty<BarrageCast>(), Day1);
        return store;
    }

    private const string AddFromFight = """
        {"type":"add-event-trigger","zone":"Zone","name":"Bossy",
         "entry":{"kind":"emote","text":"Bossy roars!","line":"\\aNPC 12 Bossy:Bossy\\/a roars!"},
         "trigger":{"regex":"Bossy\\\\/a roars!","sound":3,"soundData":" Spread ","category":"Zone","restrictToZone":true}}
        """;

    [Fact]
    public void ATriggerFromAFightAddsItsTextWithTheLine()
    {
        var store = Recorded();

        Assert.Equal("Event text and its trigger added to Bossy.", Run(store, AddFromFight).Notice);
        var text = Assert.Single(store.Encounters[0].EventTexts);
        Assert.Equal(@"\aNPC 12 Bossy:Bossy\/a roars!", text.Line);
        Assert.Equal(@"Bossy\\/a roars!", text.Regex);
        Assert.True(text.RegexFromLine);
        var trigger = Assert.Single(text.Triggers!);
        Assert.Equal("Spread", trigger.SoundData);
        Assert.Equal("2026-11-01", trigger.Added);

        // Another trigger with the same regex and category is kept next to it; an identical one isn't added twice.
        Assert.Equal("Trigger added to Bossy.", Run(store, AddFromFight.Replace("Spread", "Move")).Notice);
        Assert.Equal(new[] { "Spread", "Move" }, text.Triggers!.Select(t => t.SoundData));
        Assert.Equal("Bossy already has that trigger.", Run(store, AddFromFight).Notice);
        Assert.Equal(2, text.Triggers!.Count);
        Assert.Equal("Trigger added to Bossy.", Run(store, AddFromFight.Replace("\"category\":\"Zone\"", "\"category\":\"Other\"")).Notice);
        Assert.Equal(3, text.Triggers!.Count);

        // Remove takes only the trigger the same in every setting.
        Run(store, """{"type":"remove-event-trigger","zone":"Zone","name":"Bossy","text":"Bossy roars!","trigger":{"regex":"Bossy\\\\/a roars!","sound":3,"soundData":"Move","category":"zone","restrictToZone":true}}""");
        Assert.Equal(new[] { "Spread", "Spread" }, text.Triggers!.Select(t => t.SoundData));
        Run(store, """{"type":"remove-event-trigger","zone":"Zone","name":"Bossy","text":"Bossy roars!","trigger":{"regex":"Bossy\\\\/a roars!","sound":3,"soundData":"Spread","category":"Zone","restrictToZone":true}}""");
        Assert.Equal("Other", Assert.Single(text.Triggers!).Category);
        Run(store, """{"type":"remove-event-trigger","zone":"Zone","name":"Bossy","text":"Bossy roars!","trigger":{"regex":"Bossy\\\\/a roars!","sound":3,"soundData":"Spread","category":"Other","restrictToZone":true}}""");
        Assert.Null(text.Triggers);
    }

    [Fact]
    public void ATriggerAndItsLineSurviveSavingAndLoading()
    {
        var store = Recorded();
        Run(store, AddFromFight);
        var path = Path.Combine(Path.GetTempPath(), $"augur-trigger-{Guid.NewGuid():N}.json");
        try
        {
            store.Save(path);
            var text = Assert.Single(EncounterStore.Load(path).Encounters[0].EventTexts);
            Assert.Equal(@"\aNPC 12 Bossy:Bossy\/a roars!", text.Line);
            Assert.Equal(@"Bossy\\/a roars!", Assert.Single(text.Triggers!).Regex);
            Assert.Equal(@"Bossy\\/a roars!", text.Regex);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ATextTypedByHandGetsItsRegexFromTheEncounterNames()
    {
        var store = Recorded();
        Run(store, """{"type":"add-event-text","zone":"Zone","name":"Bossy","entry":{"kind":"emote","text":"Bossy roars!"}}""");

        var text = store.Encounters[0].EventTexts[0];
        Assert.Equal(@"Bossy\\/a roars!", text.Regex);
        Assert.False(text.RegexFromLine);
    }

    [Theory]
    [InlineData("\"regex\":\"(unclosed\"", "isn't valid")]
    [InlineData("\"regex\":\"x\",\"sound\":3", "text to speech")]
    [InlineData("\"regex\":\"x\",\"timer\":true", "spell timer")]
    [InlineData("\"regex\":\" \"", "needs a regular expression")]
    public void ATriggerActCouldNotUseIsAMessageForTheUser(string trigger, string message)
    {
        var json = """{"type":"add-event-trigger","zone":"Zone","name":"Bossy","entry":{"kind":"emote","text":"Bossy roars!"},"trigger":{""" + trigger + "}}";

        var error = Assert.Throws<ArgumentException>(() => Run(Recorded(), json));
        Assert.Contains(message, error.Message);
    }
}

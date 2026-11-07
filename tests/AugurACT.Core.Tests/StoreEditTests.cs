using AugurACT.Core.EventTexts;
using AugurACT.Core.Host;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Tests;

// The page's edits to the encounters data, as the plugin runs them: by message type, from the raw JSON.
public class StoreEditTests
{
    private static readonly DateTime Day1 = new(2026, 11, 1);

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

    [Fact]
    public void EveryEditHasItsOwnType()
    {
        Assert.Equal(StoreEditSet.All.Count, StoreEditSet.All.Select(e => e.Type).Distinct().Count());
        Assert.False(StoreEditSet.TryGet("analyze", out _));
    }

    [Fact]
    public void AnEventTextIsAddedFromThePageMessage()
    {
        var store = Recorded();
        var result = Run(store, """
            {"type":"add-event-text","zone":"Zone","name":"Bossy","entry":{"kind":"emote","text":"Bossy roars!",
             "description":"Spread","setTimer":true,"timer":{"mean":60,"spread":2,"intervals":3,"fights":[5]}}}
            """);

        Assert.Equal("Event text added to Bossy.", result.Notice);
        Assert.False(result.ChangesScanOptions);
        var text = Assert.Single(store.Encounters[0].EventTexts);
        Assert.Equal("Spread", text.Description);
        Assert.Equal(new long[] { 5 }, text.Timer!.Fights);
    }

    [Fact]
    public void BlacklistEditsChangeTheScanOptions()
    {
        var store = Recorded();
        var result = Run(store, """{"type":"blacklist-event-text","kind":"narration","text":"Heroic Surge has been triggered by: {archetype}!"}""");

        Assert.True(result.ChangesScanOptions);
        Assert.Single(store.Blacklist);
        Assert.True(Run(store, """{"type":"unblacklist-event-text","text":"Heroic Surge has been triggered by: {archetype}!"}""").ChangesScanOptions);
        Assert.Empty(store.Blacklist);
    }

    [Fact]
    public void BadInputIsAMessageForTheUser()
    {
        var store = Recorded();
        var error = Assert.Throws<ArgumentException>(() =>
            Run(store, """{"type":"add-event-text","zone":"Zone","name":"Nobody","entry":{"kind":"emote","text":"x"}}"""));
        Assert.Contains("isn't in the encounters data", error.Message);
    }
}

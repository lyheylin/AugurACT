using AugurACT.Core.Effects;
using AugurACT.Core.Host;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Tests;

// Buffs and detrimentals: the page's edits, the image folder, and reading a tooltip screenshot's layout and text.
public class EffectTests
{
    private static readonly DateTime Day1 = new(2026, 11, 1);
    private const string Shot = "0123456789abcdef0123456789abcdef.png";
    private const string Icon = "fedcba9876543210fedcba9876543210.png";

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

    private static string Save(string entry) => $$"""{"type":"save-effect","zone":"Zone","name":"Bossy","entry":{{entry}}}""";

    [Fact]
    public void AnEffectIsSavedFromThePageMessage()
    {
        var store = Recorded();
        var result = Run(store, Save($$"""
            {"kind":"detrimental","name":" Singled Out ","description":"• Inflicts piercing damage on target.",
             "notes":"  ","icon":"{{Icon}}","screenshots":["{{Shot}}","{{Shot}}"]}
            """));

        Assert.Equal("Detrimental added to Bossy.", result.Notice);
        var effect = Assert.Single(store.Encounters[0].Effects);
        Assert.Equal("Singled Out", effect.Name);
        Assert.Null(effect.Notes);
        Assert.Equal(Icon, effect.Icon);
        Assert.Equal(new[] { Shot }, effect.Screenshots);
        Assert.Equal("2026-11-01", effect.Added);
    }

    [Fact]
    public void EditingRenamesAndRemovingDeletes()
    {
        var store = Recorded();
        Run(store, Save("""{"kind":"buff","name":"Ward"}"""));
        Run(store, Save("""{"kind":"detrimental","name":"Burning Affliction"}"""));

        Assert.Equal("Buff updated in Bossy.", Run(store, Save("""{"original":"ward","kind":"buff","name":"Holy Ward","notes":"Keep it up"}""")).Notice);
        Assert.Equal(new[] { "Holy Ward", "Burning Affliction" }, store.Encounters[0].Effects.Select(e => e.Name));

        var clash = Assert.Throws<ArgumentException>(() => Run(store, Save("""{"original":"Holy Ward","kind":"buff","name":"burning affliction"}""")));
        Assert.Contains("already lists", clash.Message);

        Run(store, """{"type":"remove-effect","zone":"Zone","name":"Bossy","effect":"holy ward"}""");
        Assert.Equal("Burning Affliction", Assert.Single(store.Encounters[0].Effects).Name);
    }

    [Fact]
    public void EffectsKeepTheUsersOrder()
    {
        var store = Recorded();
        Run(store, Save("""{"kind":"buff","name":"Ward"}"""));
        Run(store, Save("""{"kind":"detrimental","name":"Pox"}"""));
        Run(store, Save("""{"kind":"detrimental","name":"Burn"}"""));
        string Names() => string.Join(",", store.Encounters[0].Effects.Select(e => e.Name));
        Assert.Equal("Ward,Pox,Burn", Names());

        Assert.Equal("", Run(store, """{"type":"move-effect","zone":"Zone","name":"Bossy","effect":"burn","offset":-1}""").Notice);
        Assert.Equal("Ward,Burn,Pox", Names());
        Run(store, """{"type":"move-effect","zone":"Zone","name":"Bossy","effect":"Ward","offset":-1}""");
        Assert.Equal("Ward,Burn,Pox", Names());

        // Editing keeps its place.
        Run(store, Save("""{"original":"Burn","kind":"detrimental","name":"Burning Affliction"}"""));
        Assert.Equal("Ward,Burning Affliction,Pox", Names());
    }

    [Fact]
    public void EventTextsMoveToo()
    {
        var store = Recorded();
        foreach (var text in new[] { "Bossy roars!", "Bossy laughs." })
        {
            Run(store, $$$"""{"type":"add-event-text","zone":"Zone","name":"Bossy","entry":{"kind":"emote","text":"{{{text}}}"}}""");
        }

        Run(store, """{"type":"move-event-text","zone":"Zone","name":"Bossy","text":"Bossy roars!","offset":1}""");
        Assert.Equal(new[] { "Bossy laughs.", "Bossy roars!" }, store.Encounters[0].EventTexts.Select(t => t.Text));
        Assert.Throws<ArgumentException>(() => Run(store, """{"type":"move-event-text","zone":"Zone","name":"Bossy","text":"Gone","offset":1}"""));
    }

    [Theory]
    [InlineData("""{"kind":"curse","name":"Pox"}""", "buff or detrimental")]
    [InlineData("""{"kind":"buff","name":"  "}""", "needs a name")]
    [InlineData("""{"kind":"buff","name":"Pox","icon":"../../ACT.exe"}""", "image name")]
    [InlineData("""{"kind":"buff","name":"Pox","screenshots":["C:\\x.png"]}""", "image name")]
    public void BadEffectsAreRefused(string entry, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => Run(Recorded(), Save(entry)));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void EffectsSurviveSavingAndLoading()
    {
        var store = Recorded();
        Run(store, Save($$"""{"kind":"buff","name":"Ward","screenshots":["{{Shot}}"]}"""));
        var path = Path.Combine(Path.GetTempPath(), $"augur-effects-{Guid.NewGuid():N}.json");
        try
        {
            store.Save(path);
            var effect = Assert.Single(EncounterStore.Load(path).Encounters[0].Effects);
            Assert.Equal(EffectKind.Buff, effect.Kind);
            Assert.Equal(new[] { Shot }, effect.Screenshots);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImagesAreNamedByContentAndUnusedOnesPruned()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"augur-images-{Guid.NewGuid():N}");
        try
        {
            var images = new EffectImages(folder);
            var kept = images.Save(new byte[] { 1, 2, 3 });
            Assert.Equal(kept, images.Save(new byte[] { 1, 2, 3 }));
            Assert.True(EffectImages.IsValidName(kept));
            var dropped = images.Save(new byte[] { 4, 5, 6 });
            var fresh = images.Save(new byte[] { 7, 8, 9 });
            var old = DateTime.UtcNow.AddDays(-2);
            File.SetLastWriteTimeUtc(Path.Combine(folder, kept), old);
            File.SetLastWriteTimeUtc(Path.Combine(folder, dropped), old);

            var store = Recorded();
            Run(store, Save($$"""{"kind":"buff","name":"Ward","icon":"{{kept}}"}"""));

            Assert.Equal(1, images.Prune(store, DateTime.UtcNow.AddDays(-1)));
            Assert.True(File.Exists(Path.Combine(folder, kept)));
            Assert.False(File.Exists(Path.Combine(folder, dropped)));
            Assert.True(File.Exists(Path.Combine(folder, fresh)), "an image newer than the cutoff may belong to an open dialog");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // ---- tooltip text ----

    private static TooltipText Read(params (string Text, double Height)[] lines) =>
        TooltipText.Read(lines.Select(l => new TooltipLine(l.Text, l.Height)).ToList());

    [Fact]
    public void TheTitleAndBulletsBecomeNameAndDescription()
    {
        var text = Read(
            ("Pri: 34", 9),
            ("Singled Out", 13),
            ("Effects:", 10),
            ("\u2022 Inflicts 54,705,687,113 piercing damage on", 10),
            ("target.", 10),
            ("\u2022 If cured while no other allies are within 12 meters of target,", 10),
            ("target dies, unless target is a scout.", 10),
            ("Target dies if cured while within 12 meters of another", 10),
            ("cursed target. And those targets die too!", 10));

        Assert.Equal("Singled Out", text.Name);
        Assert.Equal(
            "• Inflicts 54,705,687,113 piercing damage on target.\n"
            + "• If cured while no other allies are within 12 meters of target, target dies, unless target is a scout.\n"
            + "• Target dies if cured while within 12 meters of another cursed target. And those targets die too!",
            text.Description);
    }

    [Fact]
    public void AQuoteIsNotABullet()
    {
        var text = Read(("Burning Affliction", 13), ("Effec&:", 10), ("- \"Burning Affliction\" cannot be cured by", 10), ("normal means.", 10), ("'Torch' interaction may work", 10));

        Assert.Equal("• \"Burning Affliction\" cannot be cured by normal means.\n• 'Torch' interaction may work", text.Description);
    }

    [Fact]
    public void WithoutAHeaderTheFirstLinesHoldTheTitle()
    {
        var text = Read(("ll',iii", 6), ("Ward of Ages", 12), ("Absorbs damage.", 9));

        Assert.Equal("Ward of Ages", text.Name);
        Assert.Equal("• Absorbs damage.", text.Description);
    }

    [Theory]
    [InlineData("Inflicts 375 - 625% of max health", "Inflicb 37S -625% of max health")]
    [InlineData("Some way to remove it properly", "Some way to remove it proF*rly")]
    [InlineData("Inflicts 1,293,278,678 damage", "Inflicts damage")]
    public void PlausibilityPrefersCleanReadings(string better, string worse)
    {
        Assert.True(TooltipText.Plausibility(better) > TooltipText.Plausibility(worse));
    }

    // ---- tooltip layout ----

    private const int Dark = unchecked((int)0xFF101418);

    private static int[] Panel(int width, int height)
    {
        var pixels = new int[width * height];
        Array.Fill(pixels, Dark);
        return pixels;
    }

    private static void Fill(int[] pixels, int width, int x0, int y0, int w, int h, int argb)
    {
        for (var y = y0; y < y0 + h; y++)
        {
            for (var x = x0; x < x0 + w; x++)
            {
                pixels[y * width + x] = argb;
            }
        }
    }

    [Fact]
    public void TheIconIsFoundBesideTheTextAndInsideTheFrame()
    {
        const int width = 360, height = 280;
        var pixels = Panel(width, height);
        const int gold = unchecked((int)0xFFD4AF37);
        Fill(pixels, width, 0, 0, width, 2, gold);              // frame, top
        Fill(pixels, width, 0, 0, 2, height, gold);             // frame, left
        Fill(pixels, width, 300, 8, 40, 40, unchecked((int)0xFFE06020)); // icon
        Fill(pixels, width, 312, 20, 16, 16, Dark);             // a dark patch in the icon art
        Fill(pixels, width, 10, 10, 110, 14, unchecked((int)0xFFFFFF00)); // title text
        Fill(pixels, width, 20, 60, 30, 30, unchecked((int)0xFF40FF40)); // a square of text the OCR boxed

        var icon = TooltipLayout.FindIcon(pixels, width, height, new[] { new PixelRect(10, 10, 110, 14), new PixelRect(20, 60, 30, 30) });

        Assert.NotNull(icon);
        Assert.Equal("300,8 40x40", icon!.ToString());
    }

    [Fact]
    public void NoIconInAPanelOfTextOnly()
    {
        const int width = 200, height = 100;
        var pixels = Panel(width, height);
        Fill(pixels, width, 10, 10, 150, 10, unchecked((int)0xFFFFFFFF));

        Assert.Null(TooltipLayout.FindIcon(pixels, width, height, Array.Empty<PixelRect>()));
    }
}

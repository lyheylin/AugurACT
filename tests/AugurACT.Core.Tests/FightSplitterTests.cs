using AugurACT.Core.Encounters;

namespace AugurACT.Core.Tests;

public class FightSplitterTests
{
    private const long T0 = 1791593375;

    private static List<(LogFight Fight, List<string> Lines)> Split(params (int Offset, string Body)[] lines)
    {
        var fights = new List<(LogFight, List<string>)>();
        var splitter = new FightSplitter(EncounterSummary.MightMatter, fight =>
        {
            var kept = new List<string>();
            fights.Add((fight, kept));
            return kept.Add;
        });
        foreach (var (offset, body) in lines)
        {
            splitter.Feed($"({T0 + offset})[Sat Oct 10 08:49:35 2026] {body}");
        }

        splitter.Complete();
        return fights;
    }

    [Fact]
    public void AGapInDamageEndsAFight()
    {
        var fights = Split(
            (0, "Tanka hits a test add for 5 crushing damage."),
            (10, "a test add has killed Healerc."),
            (20, "Tanka hits a test add for 5 crushing damage."),
            (100, "Tanka hits Bossy for 5 crushing damage."),
            (110, "Tanka hits Bossy for 5 crushing damage."));

        Assert.Equal(2, fights.Count);
        Assert.Equal((T0, T0 + 20), (fights[0].Fight.Start, fights[0].Fight.End));
        Assert.Equal(3, fights[0].Lines.Count);
        Assert.Equal(2, fights[1].Lines.Count);
    }

    [Fact]
    public void AWarningJustBeforeThePullJoinsTheFight()
    {
        var fights = Split(
            (0, @"\aNPC 42 Bossy:Bossy\/a readies a barrage!"),
            (5, "Tanka hits Bossy for 5 crushing damage."));

        var (fight, kept) = Assert.Single(fights);
        Assert.Equal(T0, fight.Start);
        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void AZoneChangeEndsAFightAndNamesTheNext()
    {
        var fights = Split(
            (0, "You have entered Old Zone [Raid]."),
            (1, "Tanka hits Bossy for 5 crushing damage."),
            (5, "You have entered New Zone [Heroic]."),
            (6, "Tanka hits a test add for 5 crushing damage."));

        Assert.Equal(new[] { "Old Zone [Raid]", "New Zone [Heroic]" }, fights.Select(f => f.Fight.Zone));
    }
}

using AugurACT.Core.Eq2;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Tests;

public class BarrageDetectorTests
{
    private const long T0 = 1791593375;
    private const string RaidZone = "Test Zone: Test Wing [Raid]";
    private const string GroupZone = "Test Zone: Test Wing [Heroic]";

    private static IReadOnlyList<BarrageCast> Run(string? self, params (int Offset, string Body)[] lines)
    {
        var detector = new BarrageDetector(self);
        foreach (var (offset, body) in lines)
        {
            Assert.True(Eq2LogLine.TryParse($"({T0 + offset})[Sat Oct 10 08:49:35 2026] {body}", out var line));
            detector.Feed(line!);
        }

        return detector.Complete();
    }

    private static IReadOnlyList<BarrageCast> Run(params (int Offset, string Body)[] lines) => Run(null, lines);

    [Fact]
    public void RaidBarrageOnTargetOnlyIsBlocked()
    {
        var casts = Run(
            (0, $"You have entered {RaidZone}."),
            (1, @"\aNPC 42 Test Boss:Test Boss\/a readies a barrage!"),
            (1, @"\#FF00FFTest Boss prepares to unleash a barrage of attacks toward TANKA!"),
            (3, "Fighterb applies a bulwark to the group."),
            (11, @"\aNPC 42 Test Boss:Test Boss\/a releases a barrage of attacks at TANKA!"),
            (13, "Test Boss's Barrage hits Tanka for 6.7B crushing damage."),
            (13, "Test Boss's Barrage hits Tanka for 13.5B crushing damage."),
            (30, "Someone hits a mob for 1 crushing damage."));

        var cast = Assert.Single(casts);
        Assert.Equal(BarrageOutcome.Blocked, cast.Outcome);
        Assert.Equal("raid", cast.Content);
        Assert.Equal("TANKA", cast.Target);
        Assert.Equal(42, cast.NpcId);
        Assert.Equal(10, cast.LeadSeconds);
        Assert.Equal(8, cast.BulwarkLeadSeconds);
        Assert.Equal(2, cast.TargetHits);
        Assert.Equal(0, cast.SplashHits);
        Assert.Equal("Barrage", cast.DamageAbility);
    }

    [Fact]
    public void RaidBarrageThatSplashesTheGroupIsMissed()
    {
        var casts = Run(
            (0, $"You have entered {RaidZone}."),
            (1, @"\aNPC 42 Test Boss:Test Boss\/a readies a barrage!"),
            (11, @"\aNPC 42 Test Boss:Test Boss\/a releases a barrage of attacks at TANKA!"),
            (13, "Test Boss's Barrage hits Tanka for 10.5B crushing damage."),
            (14, "Test Boss's Barrage hits Healerc for 15.6B focus damage."),
            (14, "Test Boss's Barrage hits Healerc for 4.5B focus damage."),
            (14, "Test Boss's Barrage hits YOU for 3.7B focus damage."));

        var cast = Assert.Single(casts);
        Assert.Equal(BarrageOutcome.Missed, cast.Outcome);
        Assert.Equal(3, cast.SplashHits);
        Assert.Equal(2, cast.SplashPlayers);
        Assert.Equal(23.8e9, cast.SplashDamage, 1);
        Assert.Null(cast.BulwarkLeadSeconds);
    }

    [Fact]
    public void HitsOnTheLoggerCountAsTargetWhenTheLoggerIsTargeted()
    {
        var casts = Run(
            "Tanka",
            (1, @"\aNPC 42 Test Boss:Test Boss\/a readies a barrage!"),
            (11, @"\aNPC 42 Test Boss:Test Boss\/a releases a barrage of attacks at TANKA!"),
            (13, "Test Boss's Barrage hits YOU for 10.5B crushing damage."));

        Assert.Equal(BarrageOutcome.Blocked, Assert.Single(casts).Outcome);
    }

    [Fact]
    public void GroupBarrageWithNoDamageIsBlocked()
    {
        var casts = Run(
            (0, $"You have entered {GroupZone}."),
            (1, @"\aNPC 7 Mo'iana the Ravenous:Mo'iana the Ravenous\/a readies a barrage!"),
            (1, @"\#FF00FFMo'iana the Ravenous prepares to unleash a barrage of attacks!"),
            (4, "Fighterb applies a bulwark to the group."),
            (6, @"\aNPC 7 Mo'iana the Ravenous:Mo'iana the Ravenous\/a releases a barrage of attacks!"));

        var cast = Assert.Single(casts);
        Assert.Equal(BarrageOutcome.Blocked, cast.Outcome);
        Assert.Equal("group", cast.Content);
        Assert.Null(cast.Target);
        Assert.Equal(5, cast.LeadSeconds);
    }

    [Fact]
    public void GroupBarrageDamageIsMissedAndLateBulwarkIsKept()
    {
        var casts = Run(
            (0, $"You have entered {GroupZone}."),
            (1, @"\aNPC 7 Mo'iana the Ravenous:Mo'iana the Ravenous\/a readies a barrage!"),
            (6, @"\aNPC 7 Mo'iana the Ravenous:Mo'iana the Ravenous\/a releases a barrage of attacks!"),
            (7, "Mo'iana the Ravenous' Barrage: Unfathomable Foray hits Healerc for 22.3B slashing damage."),
            (7, "Mo'iana the Ravenous' Barrage: Unfathomable Foray hits YOU but fails to inflict any damage."),
            (8, "Fighterb applies a bulwark to the group."));

        var cast = Assert.Single(casts);
        Assert.Equal(BarrageOutcome.Missed, cast.Outcome);
        Assert.Equal("Barrage: Unfathomable Foray", cast.DamageAbility);
        Assert.Equal(2, cast.SplashPlayers);
        Assert.Single(cast.Bulwarks);
        Assert.Null(cast.BulwarkLeadSeconds);
    }

    [Fact]
    public void CancelledAndUnresolvedBarrages()
    {
        var casts = Run(
            (1, @"\aNPC 7 Test Boss:Test Boss\/a readies a barrage!"),
            (2, @"\#FA58ACTest Boss cancels the barrage, preferring to perform another task!"),
            (50, @"\aNPC 7 Test Boss:Test Boss\/a readies a barrage!"),
            (90, "Someone hits a mob for 1 crushing damage."));

        Assert.Equal(new[] { BarrageOutcome.Cancelled, BarrageOutcome.Unresolved }, casts.Select(c => c.Outcome));
    }

    [Fact]
    public void ColouredWarningAloneStartsTheCast()
    {
        var casts = Run(
            (1, @"\#FF00FFTest Boss prepares to unleash a barrage of attacks!"),
            (6, @"\aNPC 9 Test Boss:Test Boss\/a releases a barrage of attacks!"),
            (60, @"\aNPC 9 Test Boss:Test Boss\/a releases a barrage of attacks!"));

        Assert.Equal(2, casts.Count);
        Assert.Equal(5, casts[0].LeadSeconds);
        Assert.Equal(9, casts[0].NpcId);
        Assert.False(casts[1].WarningSeen);
        Assert.Null(casts[1].LeadSeconds);
    }

    [Fact]
    public void PlayerBarrageAbilitiesAreIgnored()
    {
        var casts = Run(
            (1, @"\aNPC 42 Test Boss:Test Boss\/a readies a barrage!"),
            (6, @"\aNPC 42 Test Boss:Test Boss\/a releases a barrage of attacks!"),
            (7, "Playerd's Barrage hits Test Boss for a critical of 1.2T slashing damage."),
            (7, "Playerd's puppet's Foretold Barrage hits Test Boss for 5.0B mental damage."));

        Assert.Equal(BarrageOutcome.Blocked, Assert.Single(casts).Outcome);
    }

    [Fact]
    public void CatalogSkipsCastsAlreadyRecorded()
    {
        var casts = Run(
            (0, $"You have entered {GroupZone}."),
            (1, @"\aNPC 7 Test Boss:Test Boss\/a readies a barrage!"),
            (6, @"\aNPC 7 Test Boss:Test Boss\/a releases a barrage of attacks!"),
            (41, @"\aNPC 7 Test Boss:Test Boss\/a readies a barrage!"),
            (46, @"\aNPC 7 Test Boss:Test Boss\/a releases a barrage of attacks!"),
            (47, "Test Boss's Barrage: Foray hits Healerc for 1B slashing damage."));

        var catalog = new BossCatalog();
        Assert.Equal(2, catalog.AddBarrageCasts(casts));
        Assert.Equal(0, catalog.AddBarrageCasts(casts));

        var mechanic = Assert.Single(Assert.Single(catalog.Bosses).Mechanics);
        Assert.Equal(2, mechanic.Casts);
        Assert.Equal(1, mechanic.Blocked);
        Assert.Equal(1, mechanic.Missed);
        Assert.Equal(40, mechanic.IntervalSeconds);
        Assert.Equal(5, mechanic.WarningSeconds);
        Assert.Equal("untargeted", mechanic.Variant);

        var path = Path.GetTempFileName();
        try
        {
            catalog.Save(path);
            var loaded = BossCatalog.Load(path);
            Assert.Equal(0, loaded.AddBarrageCasts(casts));
            Assert.Equal(2, loaded.Bosses[0].Mechanics[0].Casts);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

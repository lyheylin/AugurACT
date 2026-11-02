using AugurACT.Core.Eq2;

namespace AugurACT.Core.Tests;

public class LogEventParserTests
{
    private static LogEvent Parse(string body)
    {
        Assert.True(Eq2LogLine.TryParse($"(1791593375)[Sat Oct 10 08:49:35 2026] {body}", out var line));
        return LogEventParser.Parse(line!);
    }

    [Fact]
    public void ParsesAbilityDamage()
    {
        var e = Parse("Test Boss's Surging Pulse hits Player for 1.2B magic damage.");

        Assert.Equal(LogEventKind.Damage, e.Kind);
        Assert.Equal("Test Boss", e.Source);
        Assert.Equal("Surging Pulse", e.Ability);
        Assert.Equal("Player", e.Target);
        Assert.Equal(1.2e9, e.Amount!.Value, 1);
        Assert.Equal("magic", e.DamageType);
        Assert.False(e.IsCritical);
    }

    [Fact]
    public void ParsesCriticalDamageAndFirstPerson()
    {
        var e = Parse("YOUR Exacting Venom hits a test mob for a Fabled critical of 420.3B poison damage.");

        Assert.Equal("YOU", e.Source);
        Assert.Equal("Exacting Venom", e.Ability);
        Assert.Equal("a test mob", e.Target);
        Assert.True(e.IsCritical);
    }

    [Fact]
    public void ParsesMeleeWithoutAbility()
    {
        var e = Parse("a test mob aoe attacks Player for 123 slashing damage.");

        Assert.Equal(LogEventKind.Damage, e.Kind);
        Assert.Equal("a test mob", e.Source);
        Assert.Null(e.Ability);
    }

    [Fact]
    public void SumsMultipleDamageTypes()
    {
        var e = Parse("Player's Ability hits Boss for 1K fire and 2K cold damage.");

        Assert.Equal(3000, e.Amount);
        Assert.Equal("fire", e.DamageType);
    }

    [Fact]
    public void ParsesWardWithBleedThrough()
    {
        var e = Parse("Player's Shroud absorbs 1.2B points of damage from being done to Tank with 1064402040 points of damage bleeding through. (98148294656 points remaining)");

        Assert.Equal(LogEventKind.Ward, e.Kind);
        Assert.Equal("Shroud", e.Ability);
        Assert.Equal("Tank", e.Target);
        Assert.Equal(1.2e9, e.Amount!.Value, 1);
    }

    [Theory]
    [InlineData("Player's Ability heals Other for 1.4B hit points.", LogEventKind.Heal)]
    [InlineData("Player reduces the damage from Boss to Other by 476528345.", LogEventKind.DamageReduction)]
    [InlineData("Player's Serenade reduces THEIR hate with Boss for a critical of 7,438,675,968 threat.", LogEventKind.Threat)]
    [InlineData("Boss's Pulse crushes Player draining 123 points of power.", LogEventKind.PowerDrain)]
    [InlineData("Player's Mail relieves Channeled Fury IV from Other.", LogEventKind.Cure)]
    [InlineData("Boss has killed Player.", LogEventKind.Kill)]
    [InlineData("Player was interrupted!", LogEventKind.Interrupt)]
    [InlineData("You have entered Test Zone.", LogEventKind.Zone)]
    [InlineData("You say, \"hello\"", LogEventKind.Chat)]
    [InlineData("\\aPC -1 Player:Player\\/a tells General (3), \"this hits you for 5 damage.\"", LogEventKind.Chat)]
    [InlineData("Boss is struck with a holy weapon!", LogEventKind.Unrecognized)]
    public void ClassifiesLineKinds(string body, LogEventKind expected)
    {
        Assert.Equal(expected, Parse(body).Kind);
    }

    [Fact]
    public void ParsesNpcSpeechWithInstanceId()
    {
        var e = Parse("\\aNPC 4773594 Test Boss:Test Boss\\/a says, \"Quiwot Pillk!\"");

        Assert.Equal(LogEventKind.NpcSpeech, e.Kind);
        Assert.Equal(4773594, e.NpcId);
        Assert.Equal("Test Boss", e.Source);
        Assert.Equal("Quiwot Pillk!", e.Text);
    }

    [Fact]
    public void ParsesNpcEmote()
    {
        var e = Parse("\\aNPC 61924 Test Boss:Test Boss\\/a releases a barrage of attacks at PLAYER!");

        Assert.Equal(LogEventKind.NpcEmote, e.Kind);
        Assert.Equal(61924, e.NpcId);
        Assert.Equal("releases a barrage of attacks at PLAYER!", e.Text);
    }

    [Theory]
    [InlineData("1.2B", 1.2e9)]
    [InlineData("875.7M", 875.7e6)]
    [InlineData("7,438,675,968", 7438675968)]
    [InlineData("476528345", 476528345)]
    public void ParsesScaledNumbers(string text, double expected)
    {
        Assert.True(ScaledNumber.TryParse(text, out var value));
        Assert.Equal(expected, value, 1);
    }
}

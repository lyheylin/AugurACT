using AugurACT.Core.Encounters;
using AugurACT.Core.Eq2;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Tests;

public class DeathDetectorTests
{
    private const long T0 = 1791593375;

    // Through EncounterSummary.Build, which feeds the shared PlayerRoster as well as the detector.
    private static IReadOnlyList<PlayerDeath> Run(string? self, IEnumerable<string>? players, params (int Offset, string Body)[] lines) =>
        Build(self, players, lines).Deaths;

    // Players always use named abilities; these characters do, before each test's own lines. Anyone else
    // (Bossy, Gantik, Gobartik) never does, like a boss in these fixtures or a named pet.
    private static readonly string[] Casters = { "Healerc", "Tanka", "Lostsoul", "Quietguy", "Grouped", "Strangr" };

    private static EncounterSummary Build(string? self, IEnumerable<string>? players, params (int Offset, string Body)[] lines) =>
        EncounterSummary.Build(
            "Test",
            "Test Zone",
            Casters.Select(c => $"({T0 - 1})[Sat Oct 10 08:49:34 2026] {c}'s Strike hits a test add for 1 crushing damage.")
                .Concat(lines.Select(l => $"({T0 + l.Offset})[Sat Oct 10 08:49:35 2026] {l.Body}")),
            self,
            players);

    private static IReadOnlyList<PlayerDeath> Run(params (int Offset, string Body)[] lines) => Run(null, null, lines);

    [Fact]
    public void PlayerKilledByAnNpcIsADeath()
    {
        var deaths = Run((5, "Test Boss, the Tester has killed Healerc."));

        var death = Assert.Single(deaths);
        Assert.Equal("Healerc", death.Name);
        Assert.Equal("Test Boss, the Tester", death.Killer);
        Assert.Equal(T0 + 5, death.At);
        Assert.Single(death.Lines);
    }

    [Fact]
    public void KillAndPainLinesForOneDeathAreMerged()
    {
        var deaths = Run(
            (5, "Alas, Healerc has died from pain and suffering."),
            (5, "a test add has killed Healerc."),
            (7, "Alas, Tanka has died from pain and suffering."),
            (8, "a test add has killed Tanka."));

        Assert.Equal(new[] { "Healerc", "Tanka" }, deaths.Select(d => d.Name));
        Assert.All(deaths, d => Assert.Equal("a test add", d.Killer));
        Assert.All(deaths, d => Assert.Equal(2, d.Lines.Count));
    }

    [Fact]
    public void DyingAgainAfterARezCountsTwice()
    {
        var deaths = Run(
            (5, "a test add has killed Healerc."),
            (40, "a test add has killed Healerc."));

        Assert.Equal(2, deaths.Count);
    }

    [Fact]
    public void LoggerDeathsUseTheLoggersName()
    {
        var deaths = Run("Selfy", null,
            (5, "Test Boss has killed you."),
            (5, "Alas, Selfy has died from pain and suffering."),
            (60, "Alas, Selfy has died from pain and suffering."));

        Assert.Equal(2, deaths.Count);
        Assert.All(deaths, d => Assert.Equal("Selfy", d.Name));
        Assert.Equal("Test Boss", deaths[0].Killer);
        Assert.Null(deaths[1].Killer);
    }

    [Fact]
    public void LoggerWithoutAKnownNameIsYou()
    {
        var death = Assert.Single(Run((5, "a test add has killed you.")));
        Assert.Equal("YOU", death.Name);
    }

    [Fact]
    public void NpcsAndPetsDyingAreNotDeaths()
    {
        var deaths = Run(
            (1, "Healerc has killed a test add."),
            (2, "You have killed an ancient sentry."),
            (3, "Healerc has killed Ancestral Sentry."),
            (4, "Test Boss has killed Healerc's puppet."),
            (5, "Alas, Healerc's thug has died from pain and suffering."),
            (6, "Alas, a test add has died from pain and suffering."));

        Assert.Empty(deaths);
    }

    [Fact]
    public void OneWordBossKillingAPlayerIsADeathButAPlayerKillingItIsNot()
    {
        // Healerc is shown to be a player by an obvious NPC killing them; Bossy is a one-word boss.
        var deaths = Run(
            (1, "a test add has killed Healerc."),
            (30, "Bossy has killed Tanka."),
            (40, "Healerc has killed Bossy."));

        Assert.Equal(new[] { "Healerc", "Tanka" }, deaths.Select(d => d.Name));
    }

    [Fact]
    public void LinkedNpcIsNotAPlayer()
    {
        var deaths = Run(
            (1, @"\aNPC 42 Bossy:Bossy\/a says, ""You will fall!"""),
            (5, "Somebody has killed Bossy."));

        Assert.Empty(deaths);
    }

    [Fact]
    public void PainDeathCountsOnlyForAKnownPlayer()
    {
        var deaths = Run(
            (1, @"\aPC -1 Healerc:Healerc\/a says to the raid party, ""inc"""),
            (5, "Alas, Healerc has died from pain and suffering."),
            (5, "Alas, Gobartik has died from pain and suffering."));

        Assert.Equal("Healerc", Assert.Single(deaths).Name);
    }

    [Fact]
    public void KnownPlayersDecideWhoCounts()
    {
        var deaths = Run("Selfy", new[] { "Healerc", "Selfy" },
            (5, "Alas, Healerc has died from pain and suffering."),
            (6, "Healerc has killed Bossy."),
            (7, "a test add has killed Strangr."),
            (8, "a test add has killed you."));

        Assert.Equal(new[] { "Healerc", "Selfy" }, deaths.Select(d => d.Name));
    }

    [Fact]
    public void EmptyKnownPlayersFallsBackToTheLog()
    {
        var death = Assert.Single(Run(null, Array.Empty<string>(), (5, "a test add has killed Healerc.")));
        Assert.Equal("Healerc", death.Name);
    }

    [Fact]
    public void SummaryCarriesDeaths()
    {
        var lines = new[]
        {
            $"({T0})[Sat Oct 10 08:49:35 2026] Healerc's Strike hits a test add for 5 crushing damage.",
            $"({T0 + 9})[Sat Oct 10 08:49:44 2026] a test add has killed Healerc.",
        };

        var summary = EncounterSummary.Build("a test add", "Test Zone", lines);

        Assert.Equal("Healerc", Assert.Single(summary.Deaths).Name);
        Assert.Contains("\"deaths\":[{\"name\":\"Healerc\"", summary.ToJson());
    }

    [Fact]
    public void PlayerHealedByAnotherPlayerIsKnownSoTheirBossKillCounts()
    {
        // Bossy and Tanka both look like players; the heal shows Tanka is one, and Healerc too.
        var deaths = Run(
            (1, "Healerc's Healing Light heals Tanka for 1.2B hit points."),
            (30, "Bossy has killed Tanka."),
            (40, "Healerc has killed Bossy."));

        Assert.Equal("Tanka", Assert.Single(deaths).Name);
    }

    [Fact]
    public void PlayersListsEveryoneSeenEvenWithoutEvents()
    {
        var summary = Build("Selfy", null,
            (1, "Healerc's Healing Light heals Tanka for 1.2B hit points."),
            (2, "YOUR Ward heals Quietguy for 5 hit points."),
            (3, "Bossy's Mend heals Bossy for 9 hit points."),
            (4, @"\aNPC 42 Bossy:Bossy\/a says, ""Hm."""),
            (5, "a test add has killed Lostsoul."));

        Assert.Equal(new[] { "Healerc", "Lostsoul", "Quietguy", "Selfy", "Tanka" }, summary.Players);
    }

    [Fact]
    public void PlayersComeFromTheAllyListWhenThereIsOne()
    {
        var summary = Build("Selfy", new[] { "Tanka", "Selfy", "Tanka's thug", "Healerc" },
            (1, "Strangr's Healing Light heals Otherguy for 1.2B hit points."));

        Assert.Equal(new[] { "Healerc", "Selfy", "Tanka" }, summary.Players);
    }

    [Fact]
    public void CharmedPlayerSpeakingAsAnNpcIsStillAPlayer()
    {
        var summary = Build(null, null,
            (1, "Healerc's Healing Light heals Tanka for 1.2B hit points."),
            (2, @"\aNPC 277422 Tanka:Tanka\/a says, ""Your blood calls to me."""),
            (5, "Bossy has killed Tanka."));

        Assert.Equal("Tanka", Assert.Single(summary.Deaths).Name);
        Assert.Contains("Tanka", summary.Players);
    }

    [Fact]
    public void ZoneChatAndUnknownDoNotAddPlayers()
    {
        var summary = Build("Selfy", null,
            (1, @"\aPC -1 Shopper:Shopper\/a tells General (3), ""WTB stuff"""),
            (2, @"\aPC -1 Grouped:Grouped\/a says to the group, ""ready"""),
            (3, "Grouped's Field Medic heals Unknown for 1 hit points."));

        Assert.Equal(new[] { "Grouped", "Selfy" }, summary.Players);
    }

    [Fact]
    public void NamedPetsAndMercenariesAreNotPlayers()
    {
        // Gantik melees and takes heals and wards, but never uses a named ability.
        var summary = Build(null, null,
            (1, "Gantik hits Bossy for 5 piercing damage."),
            (2, "Healerc's Field Medic heals Gantik for 5 hit points."),
            (3, "Tanka's Aura of Leadership absorbs 5 points of damage from being done to Gantik."),
            (4, "a test add has killed Gantik."));

        Assert.Empty(summary.Deaths);
        Assert.DoesNotContain("Gantik", summary.Players);
        Assert.Contains("Healerc", summary.Players);
    }

    [Fact]
    public void AbilityOfANameEndingInSHasNoS()
    {
        var summary = Build(null, null,
            (1, "Arrowss' Piercing Shot hits a test add for 5 piercing damage."),
            (2, "Healerc's Field Medic heals Arrowss for 5 hit points."));

        Assert.Contains("Arrowss", summary.Players);
    }

    [Fact]
    public void AllyListStillLeavesOutNamedPets()
    {
        var summary = Build("Selfy", new[] { "Healerc", "Gantik", "Selfy" },
            (1, "Gantik hits Bossy for 5 piercing damage."));

        Assert.Equal(new[] { "Healerc", "Selfy" }, summary.Players);
    }
}

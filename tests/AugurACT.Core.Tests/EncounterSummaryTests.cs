using AugurACT.Core.Encounters;

namespace AugurACT.Core.Tests;

public class EncounterSummaryTests
{
    [Fact]
    public void CountsLinesAndFindsTimeRangeRegardlessOfOrder()
    {
        var summary = EncounterSummary.Build("Test Boss", "Test Zone", new[]
        {
            "(1791593400)[Sat Oct 10 08:50:00 2026] later line",
            "guild MOTD with no timestamp",
            "(1791593375)[Sat Oct 10 08:49:35 2026] earlier line",
        });

        Assert.Equal(3, summary.TotalLines);
        Assert.Equal(2, summary.ParsedLines);
        Assert.Equal(1791593375, summary.StartEpoch);
        Assert.Equal(1791593400, summary.EndEpoch);
    }

    [Fact]
    public void SerializesWithNullTimesWhenNothingParsed()
    {
        var json = EncounterSummary.Build("Test \"Boss\"", "Zone", new[] { "no timestamp" }).ToJson();

        Assert.Contains("\"type\":\"encounter-summary\"", json);
        Assert.Contains("\"title\":\"Test \\\"Boss\\\"\"", json);
        Assert.Contains("\"startEpoch\":null", json);
    }
}

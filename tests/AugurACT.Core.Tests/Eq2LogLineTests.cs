using AugurACT.Core.Eq2;

namespace AugurACT.Core.Tests;

public class Eq2LogLineTests
{
    [Fact]
    public void ParsesEpochAndBody()
    {
        var ok = Eq2LogLine.TryParse(
            "(1791593375)[Sat Oct 10 08:49:35 2026] Test Boss's Surging Pulse hits Player for 1.2B magic damage.",
            out var line);

        Assert.True(ok);
        Assert.Equal(1791593375, line!.EpochSeconds);
        Assert.Equal("Test Boss's Surging Pulse hits Player for 1.2B magic damage.", line.Body);
    }

    [Fact]
    public void ParsesSpacePaddedDayOfMonth()
    {
        Assert.True(Eq2LogLine.TryParse("(1793563012)[Mon Nov  2 03:56:52 2026] You have entered Test Zone.", out var line));
        Assert.Equal("You have entered Test Zone.", line!.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Raid Friday 8pm and Sunday 5pm")]
    public void RejectsLinesWithoutTimestamp(string text)
    {
        Assert.False(Eq2LogLine.TryParse(text, out var line));
        Assert.Null(line);
    }
}

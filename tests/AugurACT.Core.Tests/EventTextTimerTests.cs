using AugurACT.Core.EventTexts;

namespace AugurACT.Core.Tests;

public class EventTextTimerTests
{
    // The Arm of Lucan's Blast Furnace in a real fight: 207, 223, 215, 219, 215, 226 and 233 s apart.
    private static readonly long[] Furnace = { 0, 207, 430, 645, 864, 1079, 1305, 1538 };

    [Fact]
    public void ABossTimerThatFloatsALittleIsSteady()
    {
        var timer = EventTextTimer.Measure(Furnace)!;

        Assert.True(timer.Steady);
        Assert.Equal(8, timer.Events);
        Assert.Equal(219.7, timer.Mean);
        Assert.Equal(13.3, timer.Spread);
        Assert.Equal(207, timer.Min);
        Assert.Equal(233, timer.Max);
        Assert.InRange(timer.Variation, 0.03, 0.05);
    }

    [Fact]
    public void PlayerTriggeredTextIsNotSteady()
    {
        var timer = EventTextTimer.Measure(new long[] { 0, 30, 480, 520, 700, 760, 1200 })!;

        Assert.False(timer.Steady);
        Assert.True(timer.Variation > 0.5);
    }

    [Fact]
    public void LinesTogetherAreOneEvent()
    {
        // An AoE text logged for three players at once, every 60 s.
        var timer = EventTextTimer.Measure(new long[] { 0, 0, 1, 60, 61, 120, 180, 181, 182 })!;

        Assert.Equal(4, timer.Events);
        Assert.Equal(60, timer.Mean);
        Assert.Equal(0, timer.Spread);
        Assert.True(timer.Steady);
    }

    [Fact]
    public void ThreeEventsAreTooFewToCall()
    {
        Assert.Null(EventTextTimer.Measure(new long[] { 0, 60, 120 }));
        Assert.NotNull(EventTextTimer.Measure(new long[] { 0, 60, 120, 180 }));
    }

    [Fact]
    public void TheThresholdCanBeTuned()
    {
        // 50, 70, 60: variation about 0.17.
        var times = new long[] { 0, 50, 120, 180 };

        Assert.False(EventTextTimer.Measure(times)!.Steady);
        Assert.True(EventTextTimer.Measure(times, steadyMaxVariation: 0.2)!.Steady);
    }
}

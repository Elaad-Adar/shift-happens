using ShiftCore;

namespace ShiftCore.Tests;

/// <summary>Same cases as the Swift <c>HotkeyRulesTests</c>.</summary>
public class HotkeyRuleTests
{
    [Fact]
    public void NeedsModifierUnlessFunctionKey()
    {
        Assert.True(HotkeyRule.Allowed(true, false));
        Assert.True(HotkeyRule.Allowed(false, true));
        Assert.False(HotkeyRule.Allowed(false, false));
    }
}

public class TapDetectorTests
{
    const TapDetector.Event D = TapDetector.Event.Down, U = TapDetector.Event.Up, O = TapDetector.Event.Other;

    static int[] Fired(params (TapDetector.Event E, double T)[] events)
    {
        var d = new TapDetector();
        return events.Select((e, i) => d.Feed(e.E, e.T) ? i : -1).Where(i => i >= 0).ToArray();
    }

    [Fact]
    public void QuickDoubleTapFiresOnSecondRelease() =>
        Assert.Equal(new[] { 3 }, Fired((D, 0), (U, 0.1), (D, 0.25), (U, 0.33)));

    [Fact]
    public void SingleTapOrHoldNeverFires()
    {
        Assert.Empty(Fired((D, 0), (U, 0.1)));
        Assert.Empty(Fired((D, 0), (U, 1.0), (D, 1.1), (U, 1.2)));
        Assert.Empty(Fired((D, 0), (U, 0.1), (D, 0.2), (U, 0.9)));
    }

    [Fact]
    public void SlowSecondTapStartsOver()
    {
        Assert.Empty(Fired((D, 0), (U, 0.1), (D, 1.0), (U, 1.1)));
        Assert.Equal(new[] { 5 }, Fired((D, 0), (U, 0.1), (D, 1.0), (U, 1.1), (D, 1.2), (U, 1.3)));
    }

    [Fact]
    public void AnotherKeyInBetweenCancels()
    {
        Assert.Empty(Fired((D, 0), (O, 0.05), (U, 0.1), (D, 0.2), (U, 0.3)));
        Assert.Empty(Fired((D, 0), (U, 0.1), (O, 0.15), (D, 0.2), (U, 0.3)));
    }

    [Fact]
    public void KeyRepeatWhileHeldIsIgnored() =>
        Assert.Equal(new[] { 4 }, Fired((D, 0), (D, 0.03), (U, 0.1), (D, 0.2), (U, 0.3)));

    [Fact]
    public void TripleTapFiresOnce() =>
        Assert.Equal(new[] { 3 }, Fired((D, 0), (U, 0.1), (D, 0.2), (U, 0.3), (D, 0.4), (U, 0.5)));
}

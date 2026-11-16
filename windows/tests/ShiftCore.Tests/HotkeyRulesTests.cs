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

public class TapDetectorChordTests
{
    static int[] Fired(params (bool Target, bool Down, bool Others, double T)[] keys)
    {
        var d = new TapDetector();
        return keys.Select((k, i) => d.FeedKey(k.Target, k.Down, k.Others, k.T) ? i : -1).Where(i => i >= 0).ToArray();
    }

    [Fact]
    public void PlainDoubleTapFires() =>
        Assert.Equal(new[] { 3 }, Fired((true, true, false, 0), (true, false, false, 0.1), (true, true, false, 0.2), (true, false, false, 0.3)));

    [Fact]
    public void ChordReleaseThenOneTapDoesNotFire() =>
        Assert.Empty(Fired((true, true, false, 0), (false, true, true, 0.02), (false, false, true, 0.08),
                           (true, false, false, 0.12), (true, true, false, 0.2), (true, false, false, 0.28)));

    [Fact]
    public void TapsWhileAnotherModifierIsHeldDoNotFire() =>
        Assert.Empty(Fired((false, true, false, 0), (true, true, true, 0.05), (true, false, true, 0.1),
                           (true, true, true, 0.2), (true, false, true, 0.3), (false, false, false, 0.4)));
}

namespace ShiftCore;

/// <summary>Which key presses can be a hotkey. Same rule as the Mac app.</summary>
public static class HotkeyRule
{
    /// <summary>A hotkey needs Ctrl, Alt or Win, so typing never triggers it. Function keys (F1–F24) are
    /// the exception: they type nothing, so one alone is allowed.</summary>
    public static bool Allowed(bool hasCommandModifier, bool isFunctionKey) => hasCommandModifier || isFunctionKey;
}

/// <summary>Recognizes a double tap of one modifier key (e.g. Ctrl, Ctrl): press, release, press, release,
/// each press short, the second soon after the first, nothing else in between. Holding the key, or using it
/// with another key (Ctrl+C), never counts. Same logic as the Swift <c>TapDetector</c>.</summary>
public sealed class TapDetector
{
    public enum Event { Down, Up, Other }

    enum State { Idle, FirstDown, FirstUp, SecondDown }

    public double MaxHold { get; init; } = 0.35;
    public double MaxGap { get; init; } = 0.35;

    State _state = State.Idle;
    double _since;

    /// <summary>Feeds one event of the watched modifier (Down/Up) or anything else (Other), time in
    /// seconds. Returns true on the release that completes a double tap.</summary>
    public bool Feed(Event e, double t)
    {
        switch (e)
        {
            case Event.Other:
                _state = State.Idle;
                return false;
            case Event.Down:
                if (_state == State.FirstUp && t - _since <= MaxGap) { _state = State.SecondDown; _since = t; }
                else if (_state is State.FirstDown or State.SecondDown) { /* still held */ }
                else { _state = State.FirstDown; _since = t; }
                return false;
            default:  // Up
                if (_state == State.FirstDown && t - _since <= MaxHold) { _state = State.FirstUp; _since = t; return false; }
                bool fired = _state == State.SecondDown && t - _since <= MaxHold;
                _state = State.Idle;
                return fired;
        }
    }

    /// <summary>Feeds one key transition. <paramref name="isTarget"/>: the watched modifier went down or up.
    /// <paramref name="othersHeld"/>: another modifier is still down at that moment. Any other key, or the
    /// watched key used in a chord (Ctrl held while Shift is tapped), cancels the tap in progress.</summary>
    public bool FeedKey(bool isTarget, bool isDown, bool othersHeld, double t)
    {
        if (!isTarget || othersHeld)
        {
            _state = State.Idle;
            return false;
        }
        return Feed(isDown ? Event.Down : Event.Up, t);
    }
}

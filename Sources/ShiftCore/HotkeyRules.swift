import Foundation

/// Which key presses can be a hotkey, and the double-tap detector. Platform code maps its own key codes
/// and events onto these, so the rules are the same on Mac and Windows.
public enum HotkeyRule {
    /// A hotkey needs Ctrl, Alt or ⌘/Win, so typing never triggers it. Function keys (F1–F24) are the
    /// exception: they type nothing, so one alone is allowed.
    public static func allowed(hasCommandModifier: Bool, isFunctionKey: Bool) -> Bool {
        hasCommandModifier || isFunctionKey
    }
}

/// Recognizes a double tap of one modifier key (e.g. Ctrl, Ctrl): press, release, press, release, each
/// press short, the second soon after the first, nothing else in between. Holding the key, or using it
/// with another key (Ctrl+C), never counts.
public struct TapDetector {
    public enum Event { case down, up, other }

    public var maxHold: TimeInterval
    public var maxGap: TimeInterval

    private enum State {
        case idle
        case firstDown(TimeInterval)
        case firstUp(TimeInterval)
        case secondDown(TimeInterval)
    }

    private var state = State.idle

    public init(maxHold: TimeInterval = 0.35, maxGap: TimeInterval = 0.35) {
        self.maxHold = maxHold
        self.maxGap = maxGap
    }

    /// Feeds one event of the watched modifier (`down`/`up`) or anything else (`other`). Returns true on
    /// the release that completes a double tap.
    public mutating func feed(_ event: Event, at t: TimeInterval) -> Bool {
        switch (event, state) {
        case (.other, _):
            state = .idle
        case (.down, .firstUp(let up)) where t - up <= maxGap:
            state = .secondDown(t)
        case (.down, .firstDown), (.down, .secondDown):
            break  // still held
        case (.down, _):
            state = .firstDown(t)
        case (.up, .firstDown(let down)) where t - down <= maxHold:
            state = .firstUp(t)
        case (.up, .secondDown(let down)) where t - down <= maxHold:
            state = .idle
            return true
        case (.up, _):
            state = .idle
        }
        return false
    }
}

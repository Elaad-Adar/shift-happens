import Testing
@testable import ShiftCore

@Suite struct HotkeyRuleTests {
    @Test func needsModifierUnlessFunctionKey() {
        #expect(HotkeyRule.allowed(hasCommandModifier: true, isFunctionKey: false))
        #expect(HotkeyRule.allowed(hasCommandModifier: false, isFunctionKey: true))
        #expect(!HotkeyRule.allowed(hasCommandModifier: false, isFunctionKey: false))
    }
}

@Suite struct TapDetectorTests {
    /// Feeds (event, time) pairs and returns the indexes that fired.
    func fired(_ events: [(TapDetector.Event, Double)]) -> [Int] {
        var d = TapDetector()
        return events.enumerated().compactMap { i, e in d.feed(e.0, at: e.1) ? i : nil }
    }

    @Test func quickDoubleTapFiresOnSecondRelease() {
        #expect(fired([(.down, 0), (.up, 0.1), (.down, 0.25), (.up, 0.33)]) == [3])
    }

    @Test func singleTapOrHoldNeverFires() {
        #expect(fired([(.down, 0), (.up, 0.1)]).isEmpty)
        #expect(fired([(.down, 0), (.up, 1.0), (.down, 1.1), (.up, 1.2)]).isEmpty)  // first press held
        #expect(fired([(.down, 0), (.up, 0.1), (.down, 0.2), (.up, 0.9)]).isEmpty)  // second press held
    }

    @Test func slowSecondTapStartsOver() {
        #expect(fired([(.down, 0), (.up, 0.1), (.down, 1.0), (.up, 1.1)]).isEmpty)
        // ...and that slow tap can be the first of a new double tap.
        #expect(fired([(.down, 0), (.up, 0.1), (.down, 1.0), (.up, 1.1), (.down, 1.2), (.up, 1.3)]) == [5])
    }

    @Test func anotherKeyInBetweenCancels() {
        // Ctrl+C, then Ctrl again: not a double tap.
        #expect(fired([(.down, 0), (.other, 0.05), (.up, 0.1), (.down, 0.2), (.up, 0.3)]).isEmpty)
        #expect(fired([(.down, 0), (.up, 0.1), (.other, 0.15), (.down, 0.2), (.up, 0.3)]).isEmpty)
    }

    @Test func tripleTapFiresOnce() {
        #expect(fired([(.down, 0), (.up, 0.1), (.down, 0.2), (.up, 0.3), (.down, 0.4), (.up, 0.5)]) == [3])
    }
}

extension TapDetectorTests {
    @Test func keyRepeatWhileHeldIsIgnored() {
        #expect(fired([(.down, 0), (.down, 0.03), (.up, 0.1), (.down, 0.2), (.up, 0.3)]) == [4])
    }
}

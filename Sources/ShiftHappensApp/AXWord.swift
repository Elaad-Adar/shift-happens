import ApplicationServices
import Foundation
import ShiftCore

/// Selects the word before the cursor through Accessibility: reads the focused field's text and caret,
/// finds the word in logical order (`WordWalk.wordBefore`), and sets the selection range directly.
/// Unlike ⇧← this is right for right-to-left text. Only an `.unsupported` outcome (some web views,
/// terminals, secure fields) lets the caller fall back to the key walk.
enum AXWord {
    struct Selection {
        /// The text the app should now have selected.
        let expected: String
        /// Puts the caret back where it was (collapsed, original position).
        let restore: () -> Void
    }

    enum Outcome {
        /// The word is selected.
        case selected(Selection)
        /// The field's text was read and there is no word before the caret. Final: do not fall
        /// back to ⇧←, which in right-to-left text would select the text after the caret.
        case noWord
        /// The app does not expose its text, caret or selection setting: the caller may use ⇧←.
        case unsupported
    }

    static func select() -> Outcome {
        let system = AXUIElementCreateSystemWide()
        guard let element: AXUIElement = copy(system, kAXFocusedUIElementAttribute),
              let text: String = copy(element, kAXValueAttribute),
              let rangeValue: AXValue = copy(element, kAXSelectedTextRangeAttribute) else { return .unsupported }
        var caret = CFRange()
        guard AXValueGetValue(rangeValue, .cfRange, &caret), caret.length == 0 else { return .unsupported }
        guard let word = WordWalk.wordBefore(caret: caret.location, in: text) else { return .noWord }

        guard setRange(element, CFRange(location: word.location, length: word.length)) else { return .unsupported }
        let original = caret
        return .selected(Selection(expected: (text as NSString).substring(with: word),
                                   restore: { _ = setRange(element, original) }))
    }

    private static func copy<T>(_ element: AXUIElement, _ attribute: String) -> T? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success,
              let value else { return nil }
        if T.self == AXUIElement.self, CFGetTypeID(value) == AXUIElementGetTypeID() { return (value as! T) }
        if T.self == AXValue.self, CFGetTypeID(value) == AXValueGetTypeID() { return (value as! T) }
        return value as? T
    }

    private static func setRange(_ element: AXUIElement, _ range: CFRange) -> Bool {
        var r = range
        guard let value = AXValueCreate(.cfRange, &r) else { return false }
        return AXUIElementSetAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, value) == .success
    }
}

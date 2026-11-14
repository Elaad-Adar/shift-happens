import Foundation

/// Finds the word just before the cursor when nothing is selected, by extending the selection one
/// character left at a time (⇧←) and reading it back with a copy after each step.
///
/// Character steps, not word steps: a word typed on the wrong layout contains punctuation that word
/// jumps stop at (`ba,g` is נשמע typed on English, the `,` key types ת), so ⌥⇧← would grab only `g`.
///
/// Pure logic: the app does the key presses and copies, and feeds each copied selection to `next`.
public struct WordWalk {
    public enum Step: Equatable {
        /// Press ⇧← once more, then copy and call `next` again.
        case extend
        /// The word is selected. If `shrink`, first press ⇧→ once to drop the one whitespace
        /// character (space, tab, line break) the last step added, then copy the final selection.
        case done(shrink: Bool)
        /// No word before the cursor (start of field, only whitespace, or longer than the cap).
        case fail
    }

    public let maxCharacters: Int
    private var last = ""
    private var steps = 0

    public init(maxCharacters: Int = 40) {
        self.maxCharacters = maxCharacters
    }

    /// `selection`: the text copied after the latest ⇧← (pass the previous text again when the
    /// copy did not change the clipboard, i.e. nothing new was selected).
    public mutating func next(_ selection: String) -> Step {
        steps += 1
        defer { last = selection }
        let hasWord = selection.contains { !$0.isWhitespace }

        if selection == last {
            // Did not grow: start of the field. A word reaching back to it is complete.
            return hasWord ? .done(shrink: false) : .fail
        }
        if !hasWord {
            // Still only the spaces right after the word (`akuo |`): keep going, within the cap.
            return steps > maxCharacters ? .fail : .extend
        }
        if let first = selection.first, first.isWhitespace {
            return .done(shrink: true)
        }
        return selection.count > maxCharacters ? .fail : .extend
    }
}

extension WordWalk {
    /// The word just before `caret` in `text`, as a UTF-16 range (what Accessibility APIs use), found
    /// with the same steps as the key walk but over the text in logical order, so right-to-left text
    /// works too. Steps never split a composed character (emoji, Hebrew with niqqud).
    public static func wordBefore(caret: Int, in text: String, maxCharacters: Int = 40) -> NSRange? {
        let ns = text as NSString
        guard caret >= 0, caret <= ns.length else { return nil }
        var walk = WordWalk(maxCharacters: maxCharacters)
        var start = caret
        for _ in 0..<(maxCharacters * 2 + 4) {
            if start > 0 { start = ns.rangeOfComposedCharacterSequence(at: start - 1).location }
            let selection = ns.substring(with: NSRange(location: start, length: caret - start))
            switch walk.next(selection) {
            case .extend:
                continue
            case .done(let shrink):
                if shrink { start = NSMaxRange(ns.rangeOfComposedCharacterSequence(at: start)) }
                return NSRange(location: start, length: caret - start)
            case .fail:
                return nil
            }
        }
        return nil
    }
}

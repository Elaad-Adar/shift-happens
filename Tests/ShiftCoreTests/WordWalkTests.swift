import Testing
@testable import ShiftCore

/// Simulates a text field: `text` with the cursor at the end, selection growing left one character
/// per ⇧←, and returns the steps WordWalk asked for plus the final selection.
func walk(_ text: String, max: Int = 40) -> (selected: String?, steps: Int) {
    var w = WordWalk(maxCharacters: max)
    let chars = Array(text)
    var length = 0
    var steps = 0
    while true {
        length = min(length + 1, chars.count)  // ⇧← (does nothing at the start of the field)
        steps += 1
        let selection = String(chars[(chars.count - length)...])
        switch w.next(selection) {
        case .extend:
            continue
        case .done(let shrink):
            if shrink { length -= 1 }
            return (String(chars[(chars.count - length)...]), steps)
        case .fail:
            return (nil, steps)
        }
    }
}

@Suite struct WordWalkTests {
    @Test(arguments: [
        ("akuo", "akuo"),                 // whole field is the word
        ("hello akuo", "akuo"),           // stops at the space
        ("hello akuo ", "akuo "),         // trailing space kept, still finds the word
        ("hello ba,g", "ba,g"),           // punctuation inside a wrong-layout word
        ("line one\nnv", "nv"),           // stops at a line break
        ("tab\tשלומ", "שלומ"),
        ("x  akuo  ", "akuo  "),
    ])
    func findsWord(text: String, expected: String) {
        #expect(walk(text).selected == expected)
    }

    @Test func emptyOrOnlySpacesFails() {
        #expect(walk("").selected == nil)
        #expect(walk("   ").selected == nil)
    }

    @Test func tooLongFails() {
        #expect(walk(String(repeating: "a", count: 41)).selected == nil)
        #expect(walk(String(repeating: "a", count: 40)).selected == String(repeating: "a", count: 40))
    }
}

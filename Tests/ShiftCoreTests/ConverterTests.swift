import Foundation
import Testing
@testable import ShiftCore

/// Repo root, found from this file's path, so tests read the shared `fixtures/` folder.
let repoRoot = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()

func macTable() throws -> LayoutTable {
    try LayoutTable.load(from: repoRoot.appendingPathComponent("fixtures/mac-abc-hebrew.json"))
}

/// Tiny dictionary spell checker, so core logic is tested without the OS.
struct FakeSpell: SpellChecking {
    let en: Set<String> = ["hello", "world", "the", "things", "i", "love", "what", "is", "this", "ok",
                           "how", "are", "you", "it", "its", "to", "whats", "up"]
    let he: Set<String> = ["שלום", "מה", "נשמע", "אני", "אוהב", "ספר", "ים", "שרק", "אם"]

    func words(_ lang: Lang) -> Set<String> { lang == .en ? en : he }

    func isCorrect(_ word: String, lang: Lang) -> Bool { words(lang).contains(word.lowercased()) }

    func guesses(for word: String, lang: Lang) -> [String] {
        words(lang).filter { TextTools.distance(Array(word.lowercased()), Array($0)) <= 2 }.sorted()
    }
}

@Suite struct LayoutTableTests {
    @Test func mapsAreKeyForKey() throws {
        let (l2h, h2l) = try macTable().maps()
        #expect(l2h["a"] == "ש")
        #expect(l2h["t"] == "א")
        #expect(l2h[","] == "ת")
        #expect(h2l["ש"] == "a")
        #expect(h2l["ם"] == "o")
        #expect(h2l["מ"] == "n")
        #expect(l2h["1"] == "1")
    }
}

/// Like the Windows en-US spell checker: for a word it does not know, it also suggests the same word
/// with a capital first letter ("vhh" -> "Vhh"), which must not count as a spelling fix (#30).
struct CapitalizingSpell: SpellChecking {
    let base = FakeSpell()
    let extraHe: Set<String> = ["היי"]

    func isCorrect(_ word: String, lang: Lang) -> Bool {
        base.isCorrect(word, lang: lang) || (lang == .he && extraHe.contains(word))
    }

    func guesses(for word: String, lang: Lang) -> [String] {
        var g = base.guesses(for: word, lang: lang)
        if lang == .en, !isCorrect(word, lang: .en) { g.insert(word.prefix(1).uppercased() + word.dropFirst(), at: 0) }
        return g
    }
}

/// A spell checker with fixed suggestions, for the cases real spell checkers give on short words.
struct GuessSpell: SpellChecking {
    let base = FakeSpell()
    let guessList: [String]

    func isCorrect(_ word: String, lang: Lang) -> Bool {
        base.isCorrect(word, lang: lang) || (lang == .he && word == "היי")
    }

    func guesses(for word: String, lang: Lang) -> [String] {
        lang == .en && word == "vhh" ? guessList : base.guesses(for: word, lang: lang)
    }
}

/// Suggests the name "London" for "londn".
struct NameSpell: SpellChecking {
    func isCorrect(_ word: String, lang: Lang) -> Bool { false }
    func guesses(for word: String, lang: Lang) -> [String] { word == "londn" ? ["London"] : [] }
}

@Suite struct ConverterTests {
    func converter() throws -> Converter { Converter(table: try macTable(), spell: FakeSpell()) }

    @Test(arguments: [
        ("akuo", "שלום"),
        ("יקךךם", "hello"),
        ("I love איןמעד", "I love things"),
        ("teh", "the"),
        ("Teh", "The"),
        ("שלומ", "שלום"),
        ("hello world", "hello world"),
        ("מה נשמע", "מה נשמע"),
        ("123", "123"),
        ("", ""),
        ("  akuo  ", "  שלום  "),
        // Sentence on the wrong layout whose words also read as short Hebrew words (#15).
        ("ים׳ שרק טםו", "how are you"),
        // Correct short sentences and single short words stay.
        ("is it ok", "is it ok"),
        ("אם", "אם"),
        ("to", "to"),
        // Contractions without the apostrophe (#14), even when the dictionary accepts them.
        ("whats", "what's"),
        ("Whats up", "What's up"),
        ("im", "I'm"),
        ("IM", "I'M"),
        ("its", "its"),
    ])
    func smart(input: String, expected: String) throws {
        #expect(try converter().convert(input).text == expected)
    }

    @Test func capitalOnlyGuessIsNotAFix() throws {
        let c = Converter(table: try macTable(), spell: CapitalizingSpell())
        let r = c.convert("vhh")
        #expect(r.text == "היי")
        #expect(r.switchTo == .hebrew)
        // A real typo still gets its fix, not a capitalized copy of itself.
        #expect(c.convert("teh").text == "the")
    }

    @Test func nameIsNotAFixForLowercase() throws {
        // The Windows spell checker suggests the name "Vah" for "vhh". A lowercase word is not a
        // misspelled name, so the real Hebrew word "היי" after the swap wins.
        let c = Converter(table: try macTable(), spell: GuessSpell(guessList: ["Vah"]))
        let r = c.convert("vhh")
        #expect(r.text == "היי")
        #expect(r.switchTo == .hebrew)
        #expect(c.convert("teh").text == "the")
        // A longer lowercase word may be a misspelled name.
        let names = Converter(table: try macTable(), spell: NameSpell())
        #expect(names.convert("londn").text == "London")
    }

    @Test func switchTarget() throws {
        let c = try converter()
        #expect(c.convert("akuo").switchTo == .hebrew)
        #expect(c.convert("יקךךם").switchTo == .latin)
        #expect(c.convert("teh").switchTo == nil)
        #expect(c.convert("hello").changed == false)
    }

    @Test func forceSwap() throws {
        let c = try converter()
        #expect(c.convert("hello", mode: .forceSwap).text == "יקךךם")
        #expect(c.convert("שלום", mode: .forceSwap).text == "akuo")
    }

    @Test func punctuationOnlyRunsFollowFullSwap() throws {
        // "akuo ." : whole selection wrong layout, so the lone "." swaps too (. key types ץ).
        #expect(try converter().convert("akuo .").text == "שלום ץ")
        // Mixed selection: the lone "." is left alone.
        #expect(try converter().convert("hello akuo .").text == "hello שלום .")
    }

    @Test func listNumbersStay() throws {
        // A numbered list copied as plain text: "1." is a list number, not a mistyped "1ץ".
        #expect(try converter().convert("1. akuo\n2. akuo").text == "1. שלום\n2. שלום")
        #expect(try converter().convert("akuo 3.5").text == "שלום 3.5")
        // Real punctuation next to a number still swaps: the / key types "." on Hebrew.
        #expect(try converter().convert("akuo 3/").text == "שלום 3.")
    }
}

@Suite struct TextToolsTests {
    @Test func distance() {
        #expect(TextTools.distance(Array("teh"), Array("the")) == 1)
        #expect(TextTools.distance(Array("wrold"), Array("world")) == 1)
        #expect(TextTools.distance(Array("abc"), Array("abc")) == 0)
    }

    @Test func hebrewFinals() {
        #expect(TextTools.fixHebrewFinals("שלומ") == "שלום")
        #expect(TextTools.fixHebrewFinals("םלך") == "מלך")
    }

    @Test func runsKeepEverything() {
        #expect(TextTools.runs("a  b\nc").joined() == "a  b\nc")
        #expect(TextTools.runs("a  b").count == 3)
    }
}

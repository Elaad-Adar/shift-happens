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
    let en: Set<String> = ["hello", "world", "the", "things", "i", "love", "what", "is", "this", "ok"]
    let he: Set<String> = ["שלום", "מה", "נשמע", "אני", "אוהב", "ספר"]

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
    ])
    func smart(input: String, expected: String) throws {
        #expect(try converter().convert(input).text == expected)
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

import Foundation
import Testing
import ShiftCore
@testable import ShiftMac

let repoRoot = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()

struct SmartCase: Decodable, CustomTestStringConvertible {
    let `in`: String
    let out: String
    let mode: String?
    let why: String
    var testDescription: String { "\(self.in) -> \(out) (\(why))" }
}

struct SmartCases: Decodable { let cases: [SmartCase] }

func loadCases() -> [SmartCase] {
    let url = repoRoot.appendingPathComponent("fixtures/smart-cases.json")
    return (try? JSONDecoder().decode(SmartCases.self, from: Data(contentsOf: url)).cases) ?? []
}

/// Spell checks are serialized on the main actor; the spell service is warmed up once.
@MainActor
@Suite(.serialized) struct MacTests {
    static let spell: MacSpellChecker = {
        let s = MacSpellChecker()
        s.warmUp()
        return s
    }()

    /// Apple ABC + Hebrew, installed on every Mac, so CI runners give the same table as this Mac.
    static func pair() throws -> MacLayouts.Pair {
        try MacLayouts.pair(latinID: "com.apple.keylayout.ABC", hebrewID: "com.apple.keylayout.Hebrew")
    }

    /// False on machines whose spell checker does not catch a sure misspelling in both languages
    /// (GitHub's macOS runners list Hebrew but have no working Hebrew dictionary).
    static let spellingWorks = spell.warmUp()

    @Test func liveLayoutMatchesFixture() throws {
        let live = MacLayouts.table(try Self.pair())
        let fixture = try LayoutTable.load(from: repoRoot.appendingPathComponent("fixtures/mac-abc-hebrew.json"))
        #expect(live == fixture, "Layouts changed: run `shift-happens --dump-layout > fixtures/mac-abc-hebrew.json`")
    }

    @Test(.enabled(if: spellingWorks, "spell checker does not work for English and Hebrew here"), arguments: loadCases())
    func smartCase(_ c: SmartCase) throws {
        let converter = Converter(table: MacLayouts.table(try Self.pair()), spell: Self.spell)
        let result = converter.convert(c.in, mode: c.mode == "force" ? .forceSwap : .smart)
        #expect(result.text == c.out, "\(result.notes.joined(separator: " | "))")
    }
}

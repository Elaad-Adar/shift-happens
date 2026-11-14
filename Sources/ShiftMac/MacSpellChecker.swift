import AppKit
import ShiftCore

/// The macOS system spell checker (has English and Hebrew built in, honours words the user taught it).
///
/// A check that the spell service cannot answer reads as "correct": while the service starts cold, and
/// forever for a language that is listed but has no working dictionary (seen on GitHub's macOS runners).
/// So each language is probed with a sure misspelling; a language that does not catch it is treated as
/// unavailable (every word unknown, no suggestions) instead of "everything is correct".
public final class MacSpellChecker: SpellChecking {
    private let checker = NSSpellChecker.shared
    private let english: String
    private static let probes: [Lang: String] = [.en: "qzxvkwj", .he: "קךששגזז"]

    /// Languages whose checker caught the probe misspelling on the last warm-up.
    public private(set) var working: Set<Lang> = [.en, .he]

    public init(english: String = "en") {
        self.english = english
    }

    private func code(_ lang: Lang) -> String { lang == .en ? english : "he" }

    private func catchesProbe(_ lang: Lang) -> Bool {
        !rawIsCorrect(Self.probes[lang]!, lang: lang)
    }

    /// Waits (at most ~3 s) for languages that worked before to answer properly again; a language that
    /// does not is marked unavailable. Languages already unavailable get one quick re-check, so a
    /// broken language costs nothing on later calls. Returns true when both languages work.
    @discardableResult
    public func warmUp() -> Bool {
        for lang in [Lang.en, .he] where !working.contains(lang) && catchesProbe(lang) {
            working.insert(lang)
        }
        for _ in 0..<30 {
            if working.allSatisfy(catchesProbe) { return working.count == 2 }
            Thread.sleep(forTimeInterval: 0.1)
        }
        working = working.filter(catchesProbe)
        return working.count == 2
    }

    private func rawIsCorrect(_ word: String, lang: Lang) -> Bool {
        let r = checker.checkSpelling(of: word, startingAt: 0, language: code(lang), wrap: false,
                                      inSpellDocumentWithTag: 0, wordCount: nil)
        return r.location == NSNotFound
    }

    public func isCorrect(_ word: String, lang: Lang) -> Bool {
        working.contains(lang) && rawIsCorrect(word, lang: lang)
    }

    public func guesses(for word: String, lang: Lang) -> [String] {
        guard working.contains(lang) else { return [] }
        let range = NSRange(location: 0, length: (word as NSString).length)
        var out: [String] = []
        if let c = checker.correction(forWordRange: range, in: word, language: code(lang),
                                      inSpellDocumentWithTag: 0) {
            out.append(c)
        }
        out += checker.guesses(forWordRange: range, in: word, language: code(lang),
                               inSpellDocumentWithTag: 0) ?? []
        return out
    }
}

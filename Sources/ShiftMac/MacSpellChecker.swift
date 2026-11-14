import AppKit
import ShiftCore

/// The macOS system spell checker (has English and Hebrew built in, honours words the user taught it).
public final class MacSpellChecker: SpellChecking {
    private let checker = NSSpellChecker.shared
    private let english: String

    public init(english: String = "en") {
        self.english = english
    }

    private func code(_ lang: Lang) -> String { lang == .en ? english : "he" }

    /// The spell service starts cold, and while it starts a check times out and reads as "correct".
    /// Ask about two sure misspellings until both come back as misspelled (at most ~3 s).
    public func warmUp() {
        for _ in 0..<30 {
            if !isCorrect("qzxvkwj", lang: .en) && !isCorrect("קךששגזז", lang: .he) { return }
            Thread.sleep(forTimeInterval: 0.1)
        }
    }

    public func isCorrect(_ word: String, lang: Lang) -> Bool {
        let r = checker.checkSpelling(of: word, startingAt: 0, language: code(lang), wrap: false,
                                      inSpellDocumentWithTag: 0, wordCount: nil)
        return r.location == NSNotFound
    }

    public func guesses(for word: String, lang: Lang) -> [String] {
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

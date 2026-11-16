import Foundation

public protocol SpellChecking {
    func isCorrect(_ word: String, lang: Lang) -> Bool
    /// Suggestions, best first. May be empty.
    func guesses(for word: String, lang: Lang) -> [String]
}

public enum Mode: Sendable {
    /// Wrong layout: swap. Right layout: fix spelling.
    case smart
    /// Swap every letter word, no dictionary check.
    case forceSwap
}

public struct Conversion: Sendable {
    public let text: String
    /// Layout the user should type in next, set only when something was swapped.
    public let switchTo: Script?
    public let swappedWords: Int
    public let fixedWords: Int
    /// One line per decision, for `--convert --verbose` and tests.
    public let notes: [String]

    public var changed: Bool { swappedWords > 0 || fixedWords > 0 }
}

/// Decides per script group (all Hebrew words, all Latin words) whether the group was typed on the
/// wrong layout (swap it) or on the right one (fix misspelled words). Words of one script were typed
/// on one layout, so they share one decision.
public final class Converter {
    private let latinToHebrew: [Character: Character]
    private let hebrewToLatin: [Character: Character]
    private let spell: SpellChecking

    public init(table: LayoutTable, spell: SpellChecking) {
        let m = table.maps()
        latinToHebrew = m.latinToHebrew
        hebrewToLatin = m.hebrewToLatin
        self.spell = spell
    }

    /// Key-for-key swap of `s`, read as typed on the layout of `from`.
    public func swap(_ s: String, from: Script) -> String {
        let map = from == .latin ? latinToHebrew : hebrewToLatin
        return String(s.map { map[$0] ?? $0 })
    }

    private struct Word {
        let index: Int          // index into runs
        let script: Script
        let core: String
        let swapped: String
        let swappedCore: String
        let origOK: Bool
        let swapOK: Bool
        /// "whats" -> "what's": typed on the right layout, missing only the apostrophe.
        let contraction: String?
    }

    private enum Decision { case swap, fix }

    public func convert(_ text: String, mode: Mode = .smart) -> Conversion {
        var runs = TextTools.runs(text)
        var words: [Word] = []
        var neutral: [Int] = []
        var notes: [String] = []

        for (i, run) in runs.enumerated() where !(run.first?.isWhitespace ?? true) {
            let (_, core, _) = TextTools.splitCore(run)
            guard let script = TextTools.script(of: core) else {
                neutral.append(i)
                continue
            }
            let swapped = swap(run, from: script)
            let swappedCore = TextTools.splitCore(swapped).core
            let contraction = mode == .forceSwap || script != .latin ? nil : Contractions.fix(core)
            let origOK = mode == .forceSwap ? false
                : (contraction != nil || spell.isCorrect(core, lang: script.lang))
            let swapOK = mode == .forceSwap ? false
                : (!swappedCore.isEmpty && TextTools.script(of: swappedCore) == script.other
                   && spell.isCorrect(swappedCore, lang: script.other.lang))
            words.append(Word(index: i, script: script, core: core, swapped: swapped,
                              swappedCore: swappedCore, origOK: origOK, swapOK: swapOK,
                              contraction: contraction))
        }

        var swappedCount: [Script: Int] = [:]
        var fixed = 0
        let groups = Dictionary(grouping: words, by: { $0.script })

        for script in [Script.latin, .hebrew] {
            guard let group = groups[script] else { continue }
            let decision = decide(group, mode: mode, notes: &notes)
            for w in group {
                // One keyboard, two intended languages ("hello akuo"): a word that clearly reads right
                // the other way (4+ letters) goes against its group's decision.
                let strongSwap = w.swapOK && !w.origOK && w.swappedCore.count >= 4
                let strongKeep = w.origOK && !w.swapOK && w.core.count >= 4
                let shouldSwap: Bool
                switch decision {
                case .swap: shouldSwap = !strongKeep
                case .fix: shouldSwap = strongSwap && closeGuess(w.core, lang: script.lang) == nil
                }
                if shouldSwap {
                    runs[w.index] = w.swapped
                    swappedCount[script, default: 0] += 1
                    notes.append("swap  \(w.core) -> \(w.swappedCore)")
                } else if let c = w.contraction {
                    let (lead, _, trail) = TextTools.splitCore(runs[w.index])
                    runs[w.index] = lead + c + trail
                    fixed += 1
                    notes.append("fix   \(w.core) -> \(c)")
                } else if w.origOK {
                    continue
                } else if let g = closeGuess(w.core, lang: script.lang) {
                    let (lead, _, trail) = TextTools.splitCore(runs[w.index])
                    runs[w.index] = lead + g + trail
                    fixed += 1
                    notes.append("fix   \(w.core) -> \(g)")
                } else {
                    notes.append("keep  \(w.core) (no close guess)")
                }
            }
        }

        // Punctuation-only runs follow the swap only when every word was swapped,
        // i.e. the whole selection was typed on the wrong layout.
        var target: Script?
        if let (from, _) = swappedCount.max(by: { $0.value < $1.value }) {
            target = from.other
            if swappedCount.values.reduce(0, +) == words.count {
                for i in neutral { runs[i] = swap(runs[i], from: from) }
            }
        }

        let total = swappedCount.values.reduce(0, +)
        return Conversion(text: runs.joined(), switchTo: target, swappedWords: total,
                          fixedWords: fixed, notes: notes)
    }

    private func decide(_ group: [Word], mode: Mode, notes: inout [String]) -> Decision {
        let script = group[0].script
        if mode == .forceSwap {
            notes.append("\(script): force swap")
            return .swap
        }
        // Evidence for "typed on the right layout": a known word (1), or a typo of 3+ letters with a
        // close spelling (0.75). Evidence for "wrong layout": the swapped word is known (1), but only
        // 0.5 for swapped words of 1-3 letters, since dictionaries accept many short letter strings.
        var asTyped = 0.0
        var afterSwap = 0.0
        for w in group {
            if w.origOK {
                asTyped += 1
            } else if w.core.count >= 3, closeGuess(w.core, lang: script.lang) != nil {
                asTyped += 0.75
            }
            if w.swapOK {
                afterSwap += w.swappedCore.count <= 3 ? 0.5 : 1
            }
        }
        let score = "as typed \(asTyped), after swap \(afterSwap)"
        // A sentence typed on the wrong layout: every word reads as a real word after the swap, and
        // something as typed is not a word. Short words alone score low on both sides ("how are you"
        // typed on Hebrew reads as two real Hebrew words and a typo), so the whole sentence decides.
        if group.count >= 2, group.allSatisfy(\.swapOK), group.contains(where: { !$0.origOK }) {
            notes.append("\(script): \(score), every word reads after swap -> swap")
            return .swap
        }
        if afterSwap > asTyped {
            notes.append("\(script): \(score) -> swap")
            return .swap
        }
        if asTyped > 0 {
            notes.append("\(script): \(score) -> fix")
            return .fix
        }
        // Nothing known and nothing close: layout gibberish (names, slang) is the likely case.
        notes.append("\(script): \(score), nothing known -> swap")
        return .swap
    }

    /// Best suggestion within a small edit distance, or nil. Hebrew final-letter mistakes count as 0.
    func closeGuess(_ word: String, lang: Lang) -> String? {
        if lang == .he {
            let f = TextTools.fixHebrewFinals(word)
            if f != word && spell.isCorrect(f, lang: .he) { return f }
        }
        let target = TextTools.fold(word)
        let maxDistance = word.count <= 4 ? 1 : 2
        var best: (d: Int, word: String)?
        for g in spell.guesses(for: word, lang: lang) where g != word {
            let d = TextTools.distance(target, TextTools.fold(g))
            if d <= maxDistance && (best == nil || d < best!.d) { best = (d, g) }
        }
        return best.map { TextTools.matchCase($0.word, like: word) }
    }
}

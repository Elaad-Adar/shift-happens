import Foundation

public enum Script: String, Sendable {
    case latin
    case hebrew

    public var other: Script { self == .latin ? .hebrew : .latin }
    public var lang: Lang { self == .latin ? .en : .he }
}

public enum Lang: String, Sendable {
    case en
    case he
}

extension Character {
    var isHebrewLetter: Bool {
        guard let s = unicodeScalars.first else { return false }
        return (0x05D0...0x05EA).contains(s.value)
    }

    var isLatinLetter: Bool {
        guard let s = unicodeScalars.first else { return false }
        return s.properties.isAlphabetic && s.value < 0x0250
    }
}

enum TextTools {
    /// Splits text into alternating runs of whitespace and non-whitespace, keeping every character.
    static func runs(_ s: String) -> [String] {
        var out: [String] = []
        var cur = ""
        var curIsSpace: Bool?
        for ch in s {
            let sp = ch.isWhitespace
            if let c = curIsSpace, c != sp {
                out.append(cur)
                cur = ""
            }
            cur.append(ch)
            curIsSpace = sp
        }
        if !cur.isEmpty { out.append(cur) }
        return out
    }

    /// Splits a token into leading punctuation, the letter core, and trailing punctuation.
    static func splitCore(_ t: String) -> (lead: String, core: String, trail: String) {
        let chars = Array(t)
        guard let first = chars.firstIndex(where: { $0.isLetter }),
              let last = chars.lastIndex(where: { $0.isLetter }) else {
            return (t, "", "")
        }
        return (String(chars[..<first]), String(chars[first...last]), String(chars[(last + 1)...]))
    }

    /// Hebrew or Latin, by majority of letters. Nil when there are no letters of either script.
    static func script(of s: String) -> Script? {
        var he = 0
        var la = 0
        for ch in s {
            if ch.isHebrewLetter { he += 1 } else if ch.isLatinLetter { la += 1 }
        }
        if he == 0 && la == 0 { return nil }
        return he > la ? .hebrew : .latin
    }

    static let finalToRegular: [Character: Character] = ["ך": "כ", "ם": "מ", "ן": "נ", "ף": "פ", "ץ": "צ"]
    static let regularToFinal: [Character: Character] = ["כ": "ך", "מ": "ם", "נ": "ן", "פ": "ף", "צ": "ץ"]

    /// Lowercase, Hebrew final letters folded to regular form. Used only for distance.
    static func fold(_ s: String) -> [Character] {
        s.lowercased().map { finalToRegular[$0] ?? $0 }
    }

    /// Hebrew word with final letters put in the right places: final form at the end, regular inside.
    static func fixHebrewFinals(_ w: String) -> String {
        var chars = Array(w)
        guard !chars.isEmpty else { return w }
        for i in chars.indices.dropLast() {
            if let r = finalToRegular[chars[i]] { chars[i] = r }
        }
        if let f = regularToFinal[chars[chars.count - 1]] { chars[chars.count - 1] = f }
        return String(chars)
    }

    /// Optimal string alignment distance (Levenshtein plus adjacent swaps).
    static func distance(_ a: [Character], _ b: [Character]) -> Int {
        let n = a.count
        let m = b.count
        if n == 0 { return m }
        if m == 0 { return n }
        var d = Array(repeating: Array(repeating: 0, count: m + 1), count: n + 1)
        for i in 0...n { d[i][0] = i }
        for j in 0...m { d[0][j] = j }
        for i in 1...n {
            for j in 1...m {
                let cost = a[i - 1] == b[j - 1] ? 0 : 1
                d[i][j] = min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + cost)
                if i > 1, j > 1, a[i - 1] == b[j - 2], a[i - 2] == b[j - 1] {
                    d[i][j] = min(d[i][j], d[i - 2][j - 2] + 1)
                }
            }
        }
        return d[n][m]
    }

    /// Gives `word` the casing pattern of `like` (ALL CAPS or Capitalized).
    static func matchCase(_ word: String, like: String) -> String {
        if like.count > 1, like == like.uppercased(), like != like.lowercased() {
            return word.uppercased()
        }
        if let f = like.first, f.isUppercase, let wf = word.first {
            return String(wf).uppercased() + word.dropFirst()
        }
        return word
    }
}

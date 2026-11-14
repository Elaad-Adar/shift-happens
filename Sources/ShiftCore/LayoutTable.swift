import Foundation

/// One physical key (with or without Shift) and the character it types on each layout.
public struct KeyEntry: Codable, Equatable, Sendable {
    public var key: Int
    public var shift: Bool
    public var latin: String
    public var hebrew: String

    public init(key: Int, shift: Bool, latin: String, hebrew: String) {
        self.key = key
        self.shift = shift
        self.latin = latin
        self.hebrew = hebrew
    }
}

/// The key-by-key table of two installed layouts, read from the OS (never typed by hand).
public struct LayoutTable: Codable, Equatable, Sendable {
    public var latinID: String
    public var hebrewID: String
    public var entries: [KeyEntry]

    public init(latinID: String, hebrewID: String, entries: [KeyEntry]) {
        self.latinID = latinID
        self.hebrewID = hebrewID
        self.entries = entries
    }

    /// Character maps in both directions. When two keys type the same character,
    /// the unshifted key wins, then the lower key code.
    public func maps() -> (latinToHebrew: [Character: Character], hebrewToLatin: [Character: Character]) {
        var l2h: [Character: Character] = [:]
        var h2l: [Character: Character] = [:]
        let ordered = entries.sorted { a, b in
            if a.shift != b.shift { return !a.shift }
            return a.key < b.key
        }
        for e in ordered {
            guard e.latin.count == 1, e.hebrew.count == 1,
                  let l = e.latin.first, let h = e.hebrew.first else { continue }
            if l2h[l] == nil { l2h[l] = h }
            if h2l[h] == nil { h2l[h] = l }
        }
        return (l2h, h2l)
    }

    public static func load(from url: URL) throws -> LayoutTable {
        try JSONDecoder().decode(LayoutTable.self, from: Data(contentsOf: url))
    }

    public func jsonData() throws -> Data {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        return try enc.encode(self)
    }
}

/// Keeps formatting when Fix or Swap runs on rich text (#26). Apps such as browsers and Electron editors
/// put an HTML copy next to the plain text, and list numbers, bullets and bold live only in the HTML.
/// The converter works on the plain text; this carries its word changes into the HTML text, leaving every
/// tag as it was.
public enum RichText {
    /// `html` with each word of `original` replaced by the word at the same place in `converted`. Nil
    /// when the HTML's text does not match `original` word for word (for example a word split by a tag),
    /// so the caller pastes plain text as before.
    public static func convertHTML(_ html: String, original: String, converted: String) -> String? {
        let from = words(original)
        let to = words(converted)
        guard !from.isEmpty, from.count == to.count else { return nil }
        var out = ""
        var k = 0
        var i = html.startIndex
        while i < html.endIndex {
            if html[i] == "<" {
                if html[i...].hasPrefix("<!--") {
                    let end = html.range(of: "-->", range: i..<html.endIndex)?.upperBound ?? html.endIndex
                    out += html[i..<end]
                    i = end
                    continue
                }
                let end = tagEnd(html, from: i)
                let tag = html[i..<end]
                out += tag
                i = end
                // Text inside these is not shown: copy it unchanged up to the closing tag.
                if let name = openingTagName(tag), rawTextTags.contains(name) {
                    let close = html.range(of: "</" + name, options: .caseInsensitive, range: i..<html.endIndex)?
                        .lowerBound ?? html.endIndex
                    out += html[i..<close]
                    i = close
                }
                continue
            }
            let end = html[i...].firstIndex(of: "<") ?? html.endIndex
            let raw = String(html[i..<end])
            i = end
            var changed = false
            var text = ""
            for token in tokens(decodeEntities(raw)) {
                if token.first!.isWhitespace {
                    text += token
                    continue
                }
                guard k < from.count, token == from[k] else { return nil }
                if to[k] != token { changed = true }
                text += to[k]
                k += 1
            }
            out += changed ? encode(text) : raw
        }
        return k == from.count ? out : nil
    }

    /// The index just past the `>` that closes the tag starting at `start`. A `>` inside a quoted attribute
    /// value (`title="a>b"`) does not close it.
    static func tagEnd(_ html: String, from start: String.Index) -> String.Index {
        var quote: Character?
        var i = html.index(after: start)
        while i < html.endIndex {
            let c = html[i]
            if let q = quote {
                if c == q { quote = nil }
            } else if c == "\"" || c == "'" {
                quote = c
            } else if c == ">" {
                return html.index(after: i)
            }
            i = html.index(after: i)
        }
        return html.endIndex
    }

    static let rawTextTags: Set<String> = ["script", "style", "title", "textarea"]

    static func words(_ s: String) -> [String] {
        tokens(s).filter { !$0.first!.isWhitespace }
    }

    /// Alternating runs of whitespace and non-whitespace, nothing dropped.
    static func tokens(_ s: String) -> [String] {
        var result: [String] = []
        var current = ""
        var currentIsSpace = false
        for c in s {
            if !current.isEmpty && c.isWhitespace != currentIsSpace {
                result.append(current)
                current = ""
            }
            currentIsSpace = c.isWhitespace
            current.append(c)
        }
        if !current.isEmpty { result.append(current) }
        return result
    }

    static func openingTagName(_ tag: Substring) -> String? {
        let body = tag.dropFirst()
        guard let first = body.first, first.isLetter else { return nil }
        return String(body.prefix { $0.isLetter || $0.isNumber }).lowercased()
    }

    /// Named entities that copied HTML uses for text (Word, Outlook and browsers), including the
    /// direction marks common in Hebrew mail. An unknown entity does not match the plain text, so the
    /// paste falls back to plain text.
    static let named: [String: String] = [
        "amp": "&", "lt": "<", "gt": ">", "quot": "\"",
        "apos": "'", "nbsp": "\u{00A0}", "lrm": "\u{200E}", "rlm": "\u{200F}",
        "zwj": "\u{200D}", "zwnj": "\u{200C}", "shy": "\u{00AD}", "lsquo": "\u{2018}",
        "rsquo": "\u{2019}", "sbquo": "\u{201A}", "ldquo": "\u{201C}", "rdquo": "\u{201D}",
        "bdquo": "\u{201E}", "ndash": "\u{2013}", "mdash": "\u{2014}", "hellip": "\u{2026}",
        "bull": "\u{2022}", "middot": "\u{00B7}", "copy": "\u{00A9}", "reg": "\u{00AE}",
        "trade": "\u{2122}", "euro": "\u{20AC}", "shekel": "\u{20AA}", "deg": "\u{00B0}",
        "times": "\u{00D7}", "laquo": "\u{00AB}", "raquo": "\u{00BB}", "ensp": "\u{2002}",
        "emsp": "\u{2003}", "thinsp": "\u{2009}",
    ]

    static func decodeEntities(_ s: String) -> String {
        guard s.contains("&") else { return s }
        var out = ""
        var i = s.startIndex
        while i < s.endIndex {
            if s[i] == "&", let semi = s[i...].prefix(12).firstIndex(of: ";") {
                let name = String(s[s.index(after: i)..<semi])
                if let value = entity(name) {
                    out += value
                    i = s.index(after: semi)
                    continue
                }
            }
            out.append(s[i])
            i = s.index(after: i)
        }
        return out
    }

    static func entity(_ name: String) -> String? {
        if let v = named[name] { return v }
        guard name.hasPrefix("#") else { return nil }
        let digits = name.dropFirst()
        let code = digits.lowercased().hasPrefix("x") ? UInt32(digits.dropFirst(), radix: 16) : UInt32(digits)
        return code.flatMap(Unicode.Scalar.init).map { String(Character($0)) }
    }

    static func encode(_ s: String) -> String {
        var out = ""
        for c in s {
            switch c {
            case "&": out += "&amp;"
            case "<": out += "&lt;"
            case ">": out += "&gt;"
            case "\u{00A0}": out += "&nbsp;"
            default: out.append(c)
            }
        }
        return out
    }
}

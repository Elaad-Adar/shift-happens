import Carbon
import Foundation
import ShiftCore

public enum MacLayoutError: Error, CustomStringConvertible {
    case notFound(String)

    public var description: String {
        switch self {
        case .notFound(let what): return "No \(what) keyboard layout is enabled in System Settings > Keyboard > Input Sources."
        }
    }
}

/// Reads the installed Latin and Hebrew keyboard layouts and builds the key table with UCKeyTranslate.
public enum MacLayouts {
    public struct Pair {
        public let latin: TISInputSource
        public let hebrew: TISInputSource
        public let latinID: String
        public let hebrewID: String
    }

    static func sources(includeAllInstalled: Bool) -> [TISInputSource] {
        let filter = [
            kTISPropertyInputSourceCategory as String: kTISCategoryKeyboardInputSource as String,
            kTISPropertyInputSourceType as String: kTISTypeKeyboardLayout as String,
        ] as CFDictionary
        guard let list = TISCreateInputSourceList(filter, includeAllInstalled)?.takeRetainedValue() else { return [] }
        return (list as NSArray).compactMap { $0 as! TISInputSource? }
    }

    static func property<T>(_ src: TISInputSource, _ key: CFString) -> T? {
        guard let p = TISGetInputSourceProperty(src, key) else { return nil }
        return Unmanaged<AnyObject>.fromOpaque(p).takeUnretainedValue() as? T
    }

    public static func id(of src: TISInputSource) -> String {
        property(src, kTISPropertyInputSourceID) ?? "?"
    }

    static func languages(of src: TISInputSource) -> [String] {
        property(src, kTISPropertyInputSourceLanguages) ?? []
    }

    /// Enabled layouts first; falls back to Apple's ABC and Hebrew if they are installed but not enabled.
    public static func findPair() throws -> Pair {
        let enabled = sources(includeAllInstalled: false)
        let hebrew = enabled.first { languages(of: $0).first == "he" }
            ?? sources(includeAllInstalled: true).first { id(of: $0) == "com.apple.keylayout.Hebrew" }
        let latin = enabled.first { id(of: $0) == "com.apple.keylayout.ABC" }
            ?? enabled.first { languages(of: $0).first == "en" }
            ?? sources(includeAllInstalled: true).first { id(of: $0) == "com.apple.keylayout.ABC" }
        guard let h = hebrew else { throw MacLayoutError.notFound("Hebrew") }
        guard let l = latin else { throw MacLayoutError.notFound("English") }
        return Pair(latin: l, hebrew: h, latinID: id(of: l), hebrewID: id(of: h))
    }

    /// Character typed by one key on one layout, or nil for keys that type nothing printable.
    public static func character(_ src: TISInputSource, keyCode: Int, shift: Bool) -> String? {
        guard let ptr = TISGetInputSourceProperty(src, kTISPropertyUnicodeKeyLayoutData) else { return nil }
        let data = Unmanaged<CFData>.fromOpaque(ptr).takeUnretainedValue()
        guard let bytes = CFDataGetBytePtr(data) else { return nil }
        var dead: UInt32 = 0
        var length = 0
        var chars = [UniChar](repeating: 0, count: 4)
        let mods: UInt32 = shift ? UInt32(shiftKey >> 8) & 0xFF : 0
        let status = bytes.withMemoryRebound(to: UCKeyboardLayout.self, capacity: 1) { layout in
            UCKeyTranslate(layout, UInt16(keyCode), UInt16(kUCKeyActionDown), mods,
                           UInt32(LMGetKbdType()), OptionBits(1 << kUCKeyTranslateNoDeadKeysBit),
                           &dead, chars.count, &length, &chars)
        }
        guard status == noErr, length > 0 else { return nil }
        let s = String(utf16CodeUnits: chars, count: length)
        guard let scalar = s.unicodeScalars.first, scalar.value >= 0x20, scalar.value != 0x7F else { return nil }
        return s
    }

    /// Key codes of the main typing block (letters, digits, punctuation, space). Return, Tab, Delete
    /// and keypad keys are left out.
    static let typingKeys: [Int] = Array(0...50).filter { ![36, 48].contains($0) }

    public static func table(_ pair: Pair) -> LayoutTable {
        var entries: [KeyEntry] = []
        for key in typingKeys {
            for shift in [false, true] {
                guard let l = character(pair.latin, keyCode: key, shift: shift),
                      let h = character(pair.hebrew, keyCode: key, shift: shift) else { continue }
                entries.append(KeyEntry(key: key, shift: shift, latin: l, hebrew: h))
            }
        }
        return LayoutTable(latinID: pair.latinID, hebrewID: pair.hebrewID, entries: entries)
    }

    public static func select(_ src: TISInputSource) {
        TISSelectInputSource(src)
    }

    public static func currentID() -> String {
        guard let cur = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue() else { return "?" }
        return id(of: cur)
    }
}

import AppKit
import Carbon
import ShiftMac

/// A key plus modifiers, stored as the Carbon key code and AppKit modifier flags.
struct Shortcut: Codable, Equatable {
    var keyCode: UInt32
    var modifiers: UInt  // NSEvent.ModifierFlags raw value, device-independent part only

    var flags: NSEvent.ModifierFlags { NSEvent.ModifierFlags(rawValue: modifiers) }

    var carbonModifiers: UInt32 {
        var m: UInt32 = 0
        if flags.contains(.command) { m |= UInt32(cmdKey) }
        if flags.contains(.option) { m |= UInt32(optionKey) }
        if flags.contains(.control) { m |= UInt32(controlKey) }
        if flags.contains(.shift) { m |= UInt32(shiftKey) }
        return m
    }

    /// ⌃⇧Space. Not ⌃⌥Space: macOS uses that for "next input source".
    static let defaultConvert = Shortcut(keyCode: UInt32(kVK_Space),
                                         modifiers: NSEvent.ModifierFlags([.control, .shift]).rawValue)

    static let keyNames: [UInt32: String] = [
        UInt32(kVK_Space): "Space", UInt32(kVK_Return): "Return", UInt32(kVK_Tab): "Tab",
        UInt32(kVK_Delete): "Delete", UInt32(kVK_Escape): "Esc",
        UInt32(kVK_LeftArrow): "←", UInt32(kVK_RightArrow): "→", UInt32(kVK_UpArrow): "↑", UInt32(kVK_DownArrow): "↓",
        UInt32(kVK_F1): "F1", UInt32(kVK_F2): "F2", UInt32(kVK_F3): "F3", UInt32(kVK_F4): "F4",
        UInt32(kVK_F5): "F5", UInt32(kVK_F6): "F6", UInt32(kVK_F7): "F7", UInt32(kVK_F8): "F8",
        UInt32(kVK_F9): "F9", UInt32(kVK_F10): "F10", UInt32(kVK_F11): "F11", UInt32(kVK_F12): "F12",
        UInt32(kVK_F13): "F13", UInt32(kVK_F14): "F14", UInt32(kVK_F15): "F15", UInt32(kVK_F16): "F16",
        UInt32(kVK_F17): "F17", UInt32(kVK_F18): "F18", UInt32(kVK_F19): "F19", UInt32(kVK_F20): "F20",
    ]

    /// Key codes of F1–F20. One of these alone may be a hotkey.
    static let functionKeys: Set<UInt32> = Set(keyNames.compactMap { $0.value.hasPrefix("F") ? $0.key : nil })

    var isFunctionKey: Bool { Self.functionKeys.contains(keyCode) }

    /// An F key with no modifier: it works in every app, so Settings warns about it.
    var isBareFunctionKey: Bool {
        isFunctionKey && flags.intersection([.command, .option, .control, .shift]).isEmpty
    }

    var display: String {
        var s = ""
        if flags.contains(.control) { s += "⌃" }
        if flags.contains(.option) { s += "⌥" }
        if flags.contains(.shift) { s += "⇧" }
        if flags.contains(.command) { s += "⌘" }
        if let name = Self.keyNames[keyCode] { return s + name }
        if let pair = try? MacLayouts.findPair(),
           let ch = MacLayouts.character(pair.latin, keyCode: Int(keyCode), shift: false) {
            return s + ch.uppercased()
        }
        return s + "key \(keyCode)"
    }
}

/// User settings in UserDefaults.
final class Settings {
    static let shared = Settings()
    private let defaults = UserDefaults.standard

    private func shortcut(_ key: String) -> Shortcut? {
        guard let data = defaults.data(forKey: key) else { return nil }
        return try? JSONDecoder().decode(Shortcut.self, from: data)
    }

    private func setShortcut(_ s: Shortcut?, _ key: String) {
        if let s, let data = try? JSONEncoder().encode(s) {
            defaults.set(data, forKey: key)
        } else {
            defaults.set(Data(), forKey: key)  // empty data = explicitly cleared
        }
    }

    /// Main hotkey: smart convert. Nil only if the user cleared it.
    var convertShortcut: Shortcut? {
        get { defaults.data(forKey: "convertShortcut") == nil ? Shortcut.defaultConvert : shortcut("convertShortcut") }
        set { setShortcut(newValue, "convertShortcut") }
    }

    /// Optional second hotkey: always swap. Unset by default.
    var forceSwapShortcut: Shortcut? {
        get { shortcut("forceSwapShortcut") }
        set { setShortcut(newValue, "forceSwapShortcut") }
    }

    /// With nothing selected, select and fix the word just before the cursor.
    var fixWordBeforeCursor: Bool {
        get { defaults.object(forKey: "fixWordBeforeCursor") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "fixWordBeforeCursor") }
    }

    /// Ask GitHub once a day whether a newer release exists.
    var checkForUpdates: Bool {
        get { defaults.object(forKey: "checkForUpdates") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "checkForUpdates") }
    }

    /// Modifier whose double tap runs Fix, or nil (default). When set, the app watches modifier-key
    /// changes (not letters) to see the double tap.
    var doubleTapKey: DoubleTapKey? {
        get { defaults.string(forKey: "doubleTapKey").flatMap(DoubleTapKey.init(rawValue:)) }
        set { defaults.set(newValue?.rawValue, forKey: "doubleTapKey") }
    }

    /// Switch the active keyboard to the language of the swapped text.
    var switchLayoutAfterSwap: Bool {
        get { defaults.object(forKey: "switchLayoutAfterSwap") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "switchLayoutAfterSwap") }
    }
}

/// Modifier keys that can be double-tapped to run Fix.
enum DoubleTapKey: String, CaseIterable {
    case control, option, shift, command

    var flag: NSEvent.ModifierFlags {
        switch self {
        case .control: return .control
        case .option: return .option
        case .shift: return .shift
        case .command: return .command
        }
    }

    /// Left and right key codes of this modifier (kVK_Control, kVK_RightControl, ...).
    var keyCodes: Set<UInt16> {
        switch self {
        case .control: return [59, 62]
        case .option: return [58, 61]
        case .shift: return [56, 60]
        case .command: return [55, 54]
        }
    }

    var symbol: String {
        switch self {
        case .control: return "⌃"
        case .option: return "⌥"
        case .shift: return "⇧"
        case .command: return "⌘"
        }
    }

    var title: String {
        switch self {
        case .control: return "⌃ Control"
        case .option: return "⌥ Option"
        case .shift: return "⇧ Shift"
        case .command: return "⌘ Command"
        }
    }
}

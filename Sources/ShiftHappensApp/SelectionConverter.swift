import AppKit
import Carbon
import ShiftCore
import ShiftMac

/// Copy the selection, convert it, paste the result, restore the clipboard, switch the keyboard.
/// Needs the Accessibility permission to send ⌘C / ⌘V.
@MainActor
final class SelectionConverter {
    private var converter: Converter?
    private var pair: MacLayouts.Pair?
    private let spell = MacSpellChecker()
    private var busy = false

    /// Last outcome, for the menu ("Swapped 3 words", "Nothing selected", ...).
    private(set) var lastStatus = ""

    init() {
        reloadLayouts()
        spell.warmUp()
    }

    /// Re-read the installed layouts (after the user adds or removes one).
    func reloadLayouts() {
        do {
            let p = try MacLayouts.findPair()
            pair = p
            converter = Converter(table: MacLayouts.table(p), spell: spell)
            lastStatus = ""
        } catch {
            pair = nil
            converter = nil
            lastStatus = "\(error)"
        }
    }

    func run(mode: Mode) {
        guard !busy else { return }
        guard let converter else {
            NSSound.beep()
            return
        }
        busy = true
        Task { @MainActor in
            defer { busy = false }
            await waitForModifiersReleased()
            // The spell service goes idle and cold again; cold answers read as "correct".
            // About 1 ms when it is already warm.
            spell.warmUp()

            let pb = NSPasteboard.general
            let saved = PasteboardSnapshot(pb)
            let before = pb.changeCount
            Keys.press(UInt16(kVK_ANSI_C), flags: .maskCommand)

            guard await waitFor({ pb.changeCount != before }, timeout: 0.5),
                  let text = pb.string(forType: .string), !text.isEmpty else {
                saved.restore(pb)
                lastStatus = "Nothing selected"
                NSSound.beep()
                return
            }

            let result = converter.convert(text, mode: mode)
            guard result.changed else {
                saved.restore(pb)
                lastStatus = "Looks right already"
                return
            }

            pb.clearContents()
            pb.setString(result.text, forType: .string)
            let ours = pb.changeCount
            Keys.press(UInt16(kVK_ANSI_V), flags: .maskCommand)

            if let target = result.switchTo, Settings.shared.switchLayoutAfterSwap, let pair {
                MacLayouts.select(target == .hebrew ? pair.hebrew : pair.latin)
            }
            lastStatus = describe(result)

            // Give the app time to read the clipboard, then put the user's clipboard back,
            // unless something else has written to it in the meantime.
            try? await Task.sleep(nanoseconds: 400_000_000)
            if pb.changeCount == ours { saved.restore(pb) }
        }
    }

    private func describe(_ r: Conversion) -> String {
        var parts: [String] = []
        if r.swappedWords > 0 { parts.append("swapped \(r.swappedWords) word\(r.swappedWords == 1 ? "" : "s")") }
        if r.fixedWords > 0 { parts.append("fixed \(r.fixedWords) word\(r.fixedWords == 1 ? "" : "s")") }
        return parts.joined(separator: ", ").capitalizedFirst
    }

    /// The hotkey's own modifiers are still down when it fires; ⌘C sent now would arrive as ⌃⇧⌘C.
    private func waitForModifiersReleased() async {
        let mask: CGEventFlags = [.maskCommand, .maskControl, .maskAlternate, .maskShift]
        _ = await waitFor({ CGEventSource.flagsState(.combinedSessionState).intersection(mask).isEmpty }, timeout: 1.5)
    }

    private func waitFor(_ condition: () -> Bool, timeout: TimeInterval) async -> Bool {
        let end = Date().addingTimeInterval(timeout)
        while Date() < end {
            if condition() { return true }
            try? await Task.sleep(nanoseconds: 10_000_000)
        }
        return condition()
    }
}

enum Keys {
    static func press(_ key: UInt16, flags: CGEventFlags) {
        let src = CGEventSource(stateID: .combinedSessionState)
        for down in [true, false] {
            guard let e = CGEvent(keyboardEventSource: src, virtualKey: key, keyDown: down) else { continue }
            e.flags = flags
            e.post(tap: .cghidEventTap)
        }
    }
}

/// Every item and type on the clipboard, so it can be put back exactly.
struct PasteboardSnapshot {
    private let items: [[NSPasteboard.PasteboardType: Data]]

    init(_ pb: NSPasteboard) {
        items = (pb.pasteboardItems ?? []).map { item in
            var d: [NSPasteboard.PasteboardType: Data] = [:]
            for t in item.types { if let data = item.data(forType: t) { d[t] = data } }
            return d
        }
    }

    func restore(_ pb: NSPasteboard) {
        pb.clearContents()
        let restored: [NSPasteboardItem] = items.map { dict in
            let item = NSPasteboardItem()
            for (t, data) in dict { item.setData(data, forType: t) }
            return item
        }
        if !restored.isEmpty { pb.writeObjects(restored) }
    }
}

extension String {
    var capitalizedFirst: String { prefix(1).uppercased() + dropFirst() }
}

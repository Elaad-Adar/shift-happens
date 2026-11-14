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
            // Sending ⌘C while the hotkey's keys are still down would arrive as e.g. ⌃⇧⌘C.
            guard await waitForModifiersReleased() else {
                lastStatus = "Release the hotkey keys to convert"
                NSSound.beep()
                return
            }
            // The spell service goes idle and cold again; cold answers read as "correct".
            // About 1 ms when it is already warm.
            spell.warmUp()

            let pb = NSPasteboard.general
            let saved = PasteboardSnapshot(pb)

            var text = await copySelection(pb, timeout: 0.5)
            var autoSelection: AutoSelection?
            if text == nil, Settings.shared.fixWordBeforeCursor {
                autoSelection = await selectWordBeforeCursor(pb)
                text = autoSelection?.text
            }
            guard let text else {
                saved.restore(pb)
                lastStatus = Settings.shared.fixWordBeforeCursor ? "No word before the cursor" : "Nothing selected"
                NSSound.beep()
                return
            }

            let result = converter.convert(text, mode: mode)
            guard result.changed else {
                // Put the cursor back where it was instead of leaving our selection behind.
                autoSelection?.restore()
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

    /// ⌘C, then the copied text, or nil when the clipboard did not change in time (nothing selected)
    /// or holds no text.
    private func copySelection(_ pb: NSPasteboard, timeout: TimeInterval) async -> String? {
        let before = pb.changeCount
        Keys.press(UInt16(kVK_ANSI_C), flags: .maskCommand)
        guard await waitFor({ pb.changeCount != before }, timeout: timeout),
              let s = pb.string(forType: .string), !s.isEmpty else { return nil }
        return s
    }

    /// A selection this app made, and how to undo it (caret back where it was).
    struct AutoSelection {
        let text: String
        let restore: () -> Void
    }

    /// Nothing selected: select the word just before the cursor. First through Accessibility (logical
    /// order, right for Hebrew); if the app does not expose its text, or the copy does not match, with
    /// ⇧← steps. Nil when there is no word; the caret is then where it was.
    private func selectWordBeforeCursor(_ pb: NSPasteboard) async -> AutoSelection? {
        if let ax = AXWord.select() {
            if let copied = await copySelection(pb, timeout: 0.5), copied == ax.expected {
                return AutoSelection(text: copied, restore: ax.restore)
            }
            ax.restore()
        }
        guard let text = await walkWithKeys(pb) else { return nil }
        return AutoSelection(text: text, restore: { Keys.press(UInt16(kVK_RightArrow), flags: []) })
    }

    /// Fallback for apps without Accessibility text: extend with ⇧← one character at a time (see
    /// WordWalk). Visual movement, so only reliable in left-to-right text. On nil the caret is where it
    /// was: a selection this made is collapsed, and nothing is pressed when none was made.
    private func walkWithKeys(_ pb: NSPasteboard) async -> String? {
        var walk = WordWalk()
        var last = ""
        steps: for _ in 0..<(walk.maxCharacters * 2 + 4) {
            Keys.press(UInt16(kVK_LeftArrow), flags: .maskShift)
            guard let selection = await copyStep(pb, hadSelection: !last.isEmpty) else { break steps }
            switch walk.next(selection) {
            case .extend:
                last = selection
            case .done(let shrink):
                guard shrink else { return selection }
                Keys.press(UInt16(kVK_RightArrow), flags: .maskShift)
                if let final = await copySelection(pb, timeout: 0.5) { return final }
                last = selection
                break steps
            case .fail:
                if !selection.isEmpty { last = selection }
                break steps
            }
        }
        if !last.isEmpty { Keys.press(UInt16(kVK_RightArrow), flags: []) }
        return nil
    }

    /// One copy during the walk, retried once for slow apps. With nothing selected yet, still no
    /// clipboard change means an empty selection (start of the field): "". With a selection already
    /// made, it is nil (abort) rather than a guess that the field boundary was reached.
    private func copyStep(_ pb: NSPasteboard, hadSelection: Bool) async -> String? {
        if let s = await copySelection(pb, timeout: 0.25) { return s }
        if let s = await copySelection(pb, timeout: 0.75) { return s }
        return hadSelection ? nil : ""
    }

    /// The hotkey's own modifiers are still down when it fires. False if they are still held after 1.5 s.
    private func waitForModifiersReleased() async -> Bool {
        let mask: CGEventFlags = [.maskCommand, .maskControl, .maskAlternate, .maskShift]
        return await waitFor({ CGEventSource.flagsState(.combinedSessionState).intersection(mask).isEmpty }, timeout: 1.5)
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

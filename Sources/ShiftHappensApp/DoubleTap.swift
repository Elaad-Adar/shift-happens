import AppKit
import ShiftCore

/// Runs an action when one modifier key is double-tapped (e.g. ⌃ ⌃). A double tap is not a hotkey macOS
/// can register, so this watches `flagsChanged` events: modifier keys going down and up, never letters.
/// Whether another key was pressed in between comes from the system's key-press counter, which counts
/// presses without saying which keys. Runs only while the user has turned the option on.
@MainActor
final class DoubleTapMonitor {
    static let shared = DoubleTapMonitor()

    private var globalMonitor: Any?
    private var localMonitor: Any?
    private var detector = TapDetector()
    private var target: NSEvent.ModifierFlags = []
    private var targetKeyCodes: Set<UInt16> = []
    private var keyPresses: UInt32 = 0
    private var action: (() -> Void)?

    var isRunning: Bool { globalMonitor != nil }

    func start(_ key: DoubleTapKey, action: @escaping () -> Void) {
        stop()
        target = key.flag
        targetKeyCodes = key.keyCodes
        self.action = action
        detector = TapDetector()
        keyPresses = Self.keyPressCount()
        // The global monitor sees other apps' events (needs the Accessibility permission the app already
        // has); the local one sees events while Shift Happens itself is in front.
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: .flagsChanged) { [weak self] event in
            self?.handle(event)
        }
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: .flagsChanged) { [weak self] event in
            self?.handle(event)
            return event
        }
    }

    func stop() {
        if let m = globalMonitor { NSEvent.removeMonitor(m) }
        if let m = localMonitor { NSEvent.removeMonitor(m) }
        globalMonitor = nil
        localMonitor = nil
        action = nil
    }

    private static func keyPressCount() -> UInt32 {
        CGEventSource.counterForEventType(.combinedSessionState, eventType: .keyDown)
    }

    private func handle(_ event: NSEvent) {
        let t = event.timestamp
        let presses = Self.keyPressCount()
        if presses != keyPresses {
            keyPresses = presses
            _ = detector.feed(.other, at: t)  // a key was typed since the last modifier change (⌃C)
        }
        // keyCode says which modifier changed; the flags say what is held now.
        let flags = event.modifierFlags.intersection([.command, .option, .control, .shift])
        let fired = detector.feedKey(isTarget: targetKeyCodes.contains(event.keyCode),
                                     isDown: flags.contains(target),
                                     othersHeld: !flags.subtracting(target).isEmpty, at: t)
        if fired { action?() }
    }
}

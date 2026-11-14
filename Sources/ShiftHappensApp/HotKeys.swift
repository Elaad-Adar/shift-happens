import Carbon

/// Global hotkeys through Carbon's RegisterEventHotKey (needs no permission).
final class HotKeys {
    static let shared = HotKeys()

    private var refs: [UInt32: EventHotKeyRef] = [:]
    private var actions: [UInt32: () -> Void] = [:]
    private var handlerInstalled = false
    private let signature: OSType = 0x5348_4850  // "SHHP"

    private func installHandler() {
        guard !handlerInstalled else { return }
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var id = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject),
                                           EventParamType(typeEventHotKeyID), nil,
                                           MemoryLayout<EventHotKeyID>.size, nil, &id)
            if status == noErr { HotKeys.shared.fire(id.id) }
            return noErr
        }, 1, &spec, nil, nil)
        handlerInstalled = true
    }

    private func fire(_ id: UInt32) {
        actions[id]?()
    }

    /// Returns false when the combination is already taken by another app.
    @discardableResult
    func register(id: UInt32, keyCode: UInt32, carbonModifiers: UInt32, action: @escaping () -> Void) -> Bool {
        installHandler()
        unregister(id: id)
        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(keyCode, carbonModifiers, EventHotKeyID(signature: signature, id: id),
                                         GetApplicationEventTarget(), 0, &ref)
        guard status == noErr, let ref else { return false }
        refs[id] = ref
        actions[id] = action
        return true
    }

    func unregister(id: UInt32) {
        if let ref = refs.removeValue(forKey: id) { UnregisterEventHotKey(ref) }
        actions[id] = nil
    }

    func unregisterAll() {
        for id in Array(refs.keys) { unregister(id: id) }
    }
}

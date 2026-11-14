import AppKit

/// Settings: the two hotkeys, keyboard switching, launch at login.
@MainActor
final class SettingsWindowController: NSWindowController, NSWindowDelegate {
    private weak var app: AppDelegate?
    private let convertRecorder = ShortcutRecorder()
    private let forceRecorder = ShortcutRecorder()
    private let switchBox = NSButton(checkboxWithTitle: "Switch keyboard to the new language after a swap", target: nil, action: nil)
    private let loginBox = NSButton(checkboxWithTitle: "Open at login", target: nil, action: nil)
    private let status = NSTextField(wrappingLabelWithString: "")

    init(app: AppDelegate) {
        self.app = app
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 260),
                              styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "Shift Happens Settings"
        super.init(window: window)
        window.delegate = self
        build()
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    private func build() {
        let s = Settings.shared
        convertRecorder.shortcut = s.convertShortcut
        forceRecorder.shortcut = s.forceSwapShortcut

        for r in [convertRecorder, forceRecorder] {
            r.onStart = { [weak self] in self?.app?.pauseHotKeys() }
            r.onEnd = { [weak self] in self?.saveShortcuts() }
        }

        let clearForce = NSButton(title: "Clear", target: self, action: #selector(clearForce))
        clearForce.bezelStyle = .rounded

        switchBox.target = self
        switchBox.action = #selector(toggleSwitch)
        switchBox.state = s.switchLayoutAfterSwap ? .on : .off
        loginBox.target = self
        loginBox.action = #selector(toggleLogin)
        loginBox.state = (app?.launchAtLogin ?? false) ? .on : .off

        status.textColor = .secondaryLabelColor
        status.font = .systemFont(ofSize: NSFont.smallSystemFontSize)

        let help = NSTextField(wrappingLabelWithString:
            "Fix: wrong keyboard → swaps the text; right keyboard → fixes spelling. Swap: always swaps.")
        help.textColor = .secondaryLabelColor
        help.font = .systemFont(ofSize: NSFont.smallSystemFontSize)

        let grid = NSGridView(views: [
            [NSTextField(labelWithString: "Fix selection:"), convertRecorder, NSGridCell.emptyContentView],
            [NSTextField(labelWithString: "Swap selection:"), forceRecorder, clearForce],
        ])
        grid.column(at: 0).xPlacement = .trailing
        grid.rowSpacing = 10
        grid.columnSpacing = 8
        convertRecorder.widthAnchor.constraint(equalToConstant: 170).isActive = true
        forceRecorder.widthAnchor.constraint(equalToConstant: 170).isActive = true

        let stack = NSStackView(views: [grid, help, switchBox, loginBox, status])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 12
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        stack.translatesAutoresizingMaskIntoConstraints = false
        help.preferredMaxLayoutWidth = 420
        status.preferredMaxLayoutWidth = 420

        let content = NSView()
        content.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            stack.topAnchor.constraint(equalTo: content.topAnchor),
            stack.bottomAnchor.constraint(equalTo: content.bottomAnchor),
        ])
        window?.contentView = content
        refreshStatus()
    }

    func show() {
        loginBox.state = (app?.launchAtLogin ?? false) ? .on : .off
        refreshStatus()
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    private func saveShortcuts() {
        Settings.shared.convertShortcut = convertRecorder.shortcut
        Settings.shared.forceSwapShortcut = forceRecorder.shortcut
        app?.registerHotKeys()
        refreshStatus()
    }

    private func refreshStatus() {
        status.stringValue = (app?.hotkeyProblems ?? []).joined(separator: "\n")
    }

    @objc private func clearForce() {
        forceRecorder.shortcut = nil
        saveShortcuts()
    }

    @objc private func toggleSwitch() {
        Settings.shared.switchLayoutAfterSwap = switchBox.state == .on
    }

    @objc private func toggleLogin() {
        do {
            try app?.setLaunchAtLogin(loginBox.state == .on)
        } catch {
            status.stringValue = "Open at login failed: \(error.localizedDescription). Move the app to /Applications and try again."
        }
        loginBox.state = (app?.launchAtLogin ?? false) ? .on : .off
    }

    func windowWillClose(_ notification: Notification) {
        convertRecorder.cancel()
        forceRecorder.cancel()
    }
}

/// Button that records the next key combination (needs ⌃, ⌥ or ⌘; Esc cancels).
@MainActor
final class ShortcutRecorder: NSButton {
    var shortcut: Shortcut? { didSet { updateTitle() } }
    var onStart: (() -> Void)?
    var onEnd: (() -> Void)?
    private var monitor: Any?

    init() {
        super.init(frame: .zero)
        bezelStyle = .rounded
        target = self
        action = #selector(startRecording)
        updateTitle()
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    private func updateTitle() {
        title = monitor != nil ? "Press keys…" : (shortcut?.display ?? "Not set")
    }

    @objc private func startRecording() {
        guard monitor == nil else { return }
        onStart?()
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self else { return event }
            if event.keyCode == 53 {  // Esc
                self.stop()
                return nil
            }
            let mods = event.modifierFlags.intersection([.command, .option, .control, .shift])
            guard !mods.intersection([.command, .option, .control]).isEmpty else {
                NSSound.beep()  // a hotkey needs ⌃, ⌥ or ⌘
                return nil
            }
            self.shortcut = Shortcut(keyCode: UInt32(event.keyCode), modifiers: mods.rawValue)
            self.stop()
            return nil
        }
        updateTitle()
    }

    func cancel() {
        if monitor != nil { stop() }
    }

    private func stop() {
        if let m = monitor { NSEvent.removeMonitor(m) }
        monitor = nil
        updateTitle()
        onEnd?()
    }
}

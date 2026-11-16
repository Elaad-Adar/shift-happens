import AppKit
import ShiftCore

/// Settings: the two hotkeys, keyboard switching, launch at login.
@MainActor
final class SettingsWindowController: NSWindowController, NSWindowDelegate {
    private weak var app: AppDelegate?
    private let convertRecorder = ShortcutRecorder()
    private let forceRecorder = ShortcutRecorder()
    private let switchBox = NSButton(checkboxWithTitle: "Switch keyboard to the new language after a swap", target: nil, action: nil)
    private let wordBox = NSButton(checkboxWithTitle: "With nothing selected, fix the word before the cursor", target: nil, action: nil)
    private let loginBox = NSButton(checkboxWithTitle: "Open at login", target: nil, action: nil)
    private let updatesBox = NSButton(checkboxWithTitle: "Check for updates once a day", target: nil, action: nil)
    private let versionLabel = NSTextField(labelWithString: "Shift Happens \(AppInfo.version)")
    private let updateStatus = NSTextField(labelWithString: "")
    private let updateButton = NSButton(title: "Check Now", target: nil, action: nil)
    private let status = NSTextField(wrappingLabelWithString: "")
    private let hint = NSTextField(wrappingLabelWithString: "")
    private let doubleTapPopup = NSPopUpButton()

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
            r.onStart = { [weak self] in
                self?.app?.pauseHotKeys()
                self?.hint.stringValue = "Press the new shortcut: ⌃, ⌥ or ⌘ with a key, or an F key alone. Esc cancels."
            }
            r.onReject = { [weak self] key in
                self?.hint.stringValue = "\(key) alone can't be a shortcut, it would fire while you type. "
                    + "Add ⌃, ⌥ or ⌘, or use an F key. Esc cancels."
            }
            r.onEnd = { [weak self] in self?.saveShortcuts() }
        }

        doubleTapPopup.addItem(withTitle: "Off")
        for key in DoubleTapKey.allCases { doubleTapPopup.addItem(withTitle: key.title) }
        if let key = s.doubleTapKey, let i = DoubleTapKey.allCases.firstIndex(of: key) {
            doubleTapPopup.selectItem(at: i + 1)
        }
        doubleTapPopup.target = self
        doubleTapPopup.action = #selector(changeDoubleTap)

        let clearForce = NSButton(title: "Clear", target: self, action: #selector(clearForce))
        clearForce.bezelStyle = .rounded
        let clearConvert = NSButton(title: "Clear", target: self, action: #selector(clearConvert))
        clearConvert.bezelStyle = .rounded

        switchBox.target = self
        switchBox.action = #selector(toggleSwitch)
        switchBox.state = s.switchLayoutAfterSwap ? .on : .off
        wordBox.target = self
        wordBox.action = #selector(toggleWord)
        wordBox.state = s.fixWordBeforeCursor ? .on : .off
        loginBox.target = self
        loginBox.action = #selector(toggleLogin)
        loginBox.state = (app?.launchAtLogin ?? false) ? .on : .off

        status.textColor = .secondaryLabelColor
        status.font = .systemFont(ofSize: NSFont.smallSystemFontSize)

        let help = NSTextField(wrappingLabelWithString:
            "Fix: typed on the wrong keyboard → swaps it; right keyboard → fixes spelling.\n"
            + "    akuo → שלום,  teh → the\n"
            + "Swap: always swaps key for key, no spell check. For when Fix guesses wrong.\n"
            + "    teh → אקי\n"
            + "Double-tap: off by default. When on, Shift Happens watches only the modifier keys "
            + "(not letters) to notice the double tap.")
        help.textColor = .secondaryLabelColor
        help.font = .systemFont(ofSize: NSFont.smallSystemFontSize)

        let grid = NSGridView(views: [
            [NSTextField(labelWithString: "Fix selection:"), convertRecorder, clearConvert],
            [NSTextField(labelWithString: "Swap selection:"), forceRecorder, clearForce],
            [NSTextField(labelWithString: "Double-tap for Fix:"), doubleTapPopup, NSGridCell.emptyContentView],
        ])
        grid.column(at: 0).xPlacement = .trailing
        grid.rowSpacing = 10
        grid.columnSpacing = 8
        convertRecorder.widthAnchor.constraint(equalToConstant: 170).isActive = true
        forceRecorder.widthAnchor.constraint(equalToConstant: 170).isActive = true

        updatesBox.target = self
        updatesBox.action = #selector(toggleUpdates)
        updatesBox.state = s.checkForUpdates ? .on : .off

        versionLabel.font = .boldSystemFont(ofSize: NSFont.systemFontSize)
        let notes = NSButton(title: "Release notes", target: self, action: #selector(openNotes))
        notes.bezelStyle = .inline
        updateButton.target = self
        updateButton.action = #selector(checkNow)
        updateButton.bezelStyle = .rounded
        updateStatus.textColor = .secondaryLabelColor
        updateStatus.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        let versionRow = NSStackView(views: [versionLabel, notes, updateButton])
        versionRow.spacing = 8

        let report = NSButton(title: "Report a Problem…", target: self, action: #selector(reportBug))
        report.bezelStyle = .rounded
        let idea = NSButton(title: "Suggest an Idea…", target: self, action: #selector(suggestIdea))
        idea.bezelStyle = .rounded
        let feedbackRow = NSStackView(views: [report, idea])
        feedbackRow.spacing = 8

        hint.textColor = .secondaryLabelColor
        hint.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        hint.preferredMaxLayoutWidth = 420

        let stack = NSStackView(views: [versionRow, updateStatus, grid, hint, help, wordBox, switchBox, loginBox,
                                        updatesBox, feedbackRow, status])
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

    private func refreshUpdate() {
        let checker = UpdateChecker.shared
        if let update = checker.available {
            updateButton.title = "Download \(update.version)…"
            updateStatus.stringValue = "Version \(update.version) is available."
        } else {
            updateButton.title = "Check Now"
            updateStatus.stringValue = checker.status
        }
    }

    @objc private func checkNow() {
        if let update = UpdateChecker.shared.available {
            NSWorkspace.shared.open(update.url)
            return
        }
        Task { await UpdateChecker.shared.check() }
    }

    @objc private func openNotes() { NSWorkspace.shared.open(Releases.notesURL(version: AppInfo.version)) }
    @objc private func reportBug() { AppInfo.openIssue(.bug) }
    @objc private func suggestIdea() { AppInfo.openIssue(.idea) }

    @objc private func toggleUpdates() {
        Settings.shared.checkForUpdates = updatesBox.state == .on
        if updatesBox.state == .on { UpdateChecker.shared.checkIfDue() }
    }

    func show() {
        UpdateChecker.shared.onChange = { [weak self] in self?.refreshUpdate() }
        refreshUpdate()
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

    /// Shown under the shortcuts: a note when an F key alone is used, else nothing.
    private func refreshHint() {
        let bare = [convertRecorder.shortcut, forceRecorder.shortcut].compactMap { $0 }.filter(\.isBareFunctionKey)
        hint.stringValue = bare.isEmpty ? "" :
            "\(bare.map(\.display).joined(separator: ", ")) alone works in every app. On a Mac keyboard press it with "
            + "Fn, or turn on “Use F1, F2, etc. keys as standard function keys” in System Settings → Keyboard."
    }

    @objc private func changeDoubleTap() {
        let i = doubleTapPopup.indexOfSelectedItem
        Settings.shared.doubleTapKey = i > 0 ? DoubleTapKey.allCases[i - 1] : nil
        app?.applyDoubleTap()
    }

    private func refreshStatus() {
        status.stringValue = (app?.hotkeyProblems ?? []).joined(separator: "\n")
        refreshHint()
    }

    /// Lets the double tap be the only way to run Fix.
    @objc private func clearConvert() {
        convertRecorder.shortcut = nil
        saveShortcuts()
    }

    @objc private func clearForce() {
        forceRecorder.shortcut = nil
        saveShortcuts()
    }

    @objc private func toggleWord() {
        Settings.shared.fixWordBeforeCursor = wordBox.state == .on
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

    func windowDidResignKey(_ notification: Notification) {
        convertRecorder.cancel()
        forceRecorder.cancel()
    }

    func windowWillClose(_ notification: Notification) {
        convertRecorder.cancel()
        forceRecorder.cancel()
    }
}

/// Button that records the next key combination (needs ⌃, ⌥ or ⌘, or is an F key; Esc cancels).
@MainActor
final class ShortcutRecorder: NSButton {
    var shortcut: Shortcut? { didSet { updateTitle() } }
    var onStart: (() -> Void)?
    var onEnd: (() -> Void)?
    /// Called with the key's name when a key without ⌃, ⌥ or ⌘ is pressed (and is not an F key).
    var onReject: ((String) -> Void)?
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
            let candidate = Shortcut(keyCode: UInt32(event.keyCode), modifiers: mods.rawValue)
            guard HotkeyRule.allowed(hasCommandModifier: !mods.intersection([.command, .option, .control]).isEmpty,
                                     isFunctionKey: candidate.isFunctionKey) else {
                NSSound.beep()
                self.onReject?(candidate.display)
                return nil
            }
            self.shortcut = candidate
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

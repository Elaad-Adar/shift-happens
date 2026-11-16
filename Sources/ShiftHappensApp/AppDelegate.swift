import AppKit
import ApplicationServices
import Carbon
import ServiceManagement
import ShiftCore

@MainActor
func runApp() {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    app.setActivationPolicy(.accessory)
    app.run()
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private var selection: SelectionConverter!
    private var settingsWindow: SettingsWindowController?
    private(set) var hotkeyProblems: [String] = []

    static let convertID: UInt32 = 1
    static let forceID: UInt32 = 2

    func applicationDidFinishLaunching(_ notification: Notification) {
        selection = SelectionConverter()

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem.button {
            button.image = MenuBarIcon.make()
        }
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu

        registerHotKeys()
        applyDoubleTap()

        // Re-read layouts when the user adds or removes an input source.
        DistributedNotificationCenter.default().addObserver(
            forName: NSNotification.Name(kTISNotifyEnabledKeyboardInputSourcesChanged as String),
            object: nil, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in self?.selection.reloadLayouts() }
        }

        if !AXIsProcessTrusted() { askForAccessibility() }

        UpdateChecker.shared.start()
    }

    // MARK: Hotkeys

    func registerHotKeys() {
        HotKeys.shared.unregisterAll()
        hotkeyProblems = []
        let s = Settings.shared
        if let c = s.convertShortcut,
           !HotKeys.shared.register(id: Self.convertID, keyCode: c.keyCode, carbonModifiers: c.carbonModifiers,
                                    action: { [weak self] in self?.convert(.smart) }) {
            hotkeyProblems.append("\(c.display) is taken by another app")
        }
        if let f = s.forceSwapShortcut,
           !HotKeys.shared.register(id: Self.forceID, keyCode: f.keyCode, carbonModifiers: f.carbonModifiers,
                                    action: { [weak self] in self?.convert(.forceSwap) }) {
            hotkeyProblems.append("\(f.display) is taken by another app")
        }
    }

    func pauseHotKeys() { HotKeys.shared.unregisterAll() }

    /// Starts or stops watching for the double-tap shortcut, per Settings (off by default).
    func applyDoubleTap() {
        if let key = Settings.shared.doubleTapKey {
            DoubleTapMonitor.shared.start(key) { [weak self] in self?.convert(.smart) }
        } else {
            DoubleTapMonitor.shared.stop()
        }
    }

    private func convert(_ mode: Mode) {
        guard AXIsProcessTrusted() else {
            askForAccessibility()
            return
        }
        selection.run(mode: mode)
    }

    // MARK: Menu (rebuilt each time it opens, so titles show current shortcuts and state)

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let s = Settings.shared

        if let update = UpdateChecker.shared.available {
            let item = NSMenuItem(title: "Update to \(update.version)…", action: #selector(openUpdate), keyEquivalent: "")
            item.target = self
            menu.addItem(item)
            menu.addItem(.separator())
        }

        // Either trigger runs Fix, so "or", not a list.
        let fixKeys = [s.convertShortcut?.display, s.doubleTapKey.map { $0.symbol + $0.symbol }].compactMap { $0 }
        let convert = NSMenuItem(title: "Fix selection" + (fixKeys.isEmpty ? "" : "  (\(fixKeys.joined(separator: " or ")))"),
                                 action: #selector(menuConvert), keyEquivalent: "")
        convert.target = self
        menu.addItem(convert)
        let force = NSMenuItem(title: "Swap selection" + suffix(s.forceSwapShortcut),
                               action: #selector(menuForce), keyEquivalent: "")
        force.target = self
        menu.addItem(force)

        let notes = [selection.lastStatus] + hotkeyProblems
            + (AXIsProcessTrusted() ? [] : ["Needs Accessibility permission"])
        for note in notes where !note.isEmpty {
            let item = NSMenuItem(title: note, action: nil, keyEquivalent: "")
            item.isEnabled = false
            menu.addItem(item)
        }
        if !AXIsProcessTrusted() {
            let ax = NSMenuItem(title: "Open Accessibility Settings…", action: #selector(openAccessibility), keyEquivalent: "")
            ax.target = self
            menu.addItem(ax)
        }

        menu.addItem(.separator())
        let settings = NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ",")
        settings.target = self
        menu.addItem(settings)
        let check = NSMenuItem(title: "Check for Updates…", action: #selector(checkUpdates), keyEquivalent: "")
        check.target = self
        menu.addItem(check)
        let bug = NSMenuItem(title: "Report a Problem…", action: #selector(reportBug), keyEquivalent: "")
        bug.target = self
        menu.addItem(bug)
        let idea = NSMenuItem(title: "Suggest an Idea…", action: #selector(suggestIdea), keyEquivalent: "")
        idea.target = self
        menu.addItem(idea)
        menu.addItem(.separator())
        let about = NSMenuItem(title: "Shift Happens \(AppInfo.version)", action: nil, keyEquivalent: "")
        about.isEnabled = false
        menu.addItem(about)
        menu.addItem(NSMenuItem(title: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
    }

    private func suffix(_ s: Shortcut?) -> String { s.map { "  (\($0.display))" } ?? "" }

    // The menu closes before the action runs, so focus is back in the user's app when ⌘C goes out.
    @objc private func menuConvert() { DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { self.convert(.smart) } }
    @objc private func menuForce() { DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { self.convert(.forceSwap) } }

    @objc private func openUpdate() {
        if let update = UpdateChecker.shared.available { NSWorkspace.shared.open(update.url) }
    }

    /// Checks now and opens Settings, where the result is shown next to the version.
    @objc private func checkUpdates() {
        openSettings()
        Task { await UpdateChecker.shared.check() }
    }

    @objc private func reportBug() { AppInfo.openIssue(.bug) }
    @objc private func suggestIdea() { AppInfo.openIssue(.idea) }

    @objc func openSettings() {
        if settingsWindow == nil { settingsWindow = SettingsWindowController(app: self) }
        settingsWindow?.show()
    }

    // MARK: Accessibility

    private func askForAccessibility() {
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    @objc private func openAccessibility() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    // MARK: Launch at login

    var launchAtLogin: Bool {
        SMAppService.mainApp.status == .enabled
    }

    func setLaunchAtLogin(_ on: Bool) throws {
        if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
    }
}

import AppKit
import ShiftCore

/// The app's own version and a one-line description of this Mac, for the Settings window, the update
/// check and prefilled issue forms.
enum AppInfo {
    static var version: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev"
    }

    static var osDescription: String {
        let v = ProcessInfo.processInfo.operatingSystemVersion
        var arch = "Apple Silicon"
        #if arch(x86_64)
        arch = "Intel"
        #endif
        return "macOS \(v.majorVersion).\(v.minorVersion).\(v.patchVersion) (\(arch))"
    }

    static func openIssue(_ kind: Releases.IssueKind) {
        NSWorkspace.shared.open(Releases.issueURL(kind, version: version, os: osDescription))
    }
}

/// Asks GitHub once a day whether a newer release exists. Only the public `releases/latest` API is
/// called; nothing about the user is sent. Opening the release page is the update: the app is not
/// signed, so it does not replace itself.
@MainActor
final class UpdateChecker {
    static let shared = UpdateChecker()

    private(set) var latest: Releases.Latest?
    private(set) var status = ""
    private var timer: Timer?
    var onChange: (() -> Void)?

    private let lastCheckKey = "lastUpdateCheck"
    private let interval: TimeInterval = 24 * 60 * 60

    /// A newer release than this app, if the last check found one.
    var available: Releases.Latest? {
        guard let latest, Releases.isNewer(latest.version, than: AppInfo.version) else { return nil }
        return latest
    }

    /// Checks shortly after launch and then hourly, each time only if a day has passed since the last check.
    func start() {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 60 * 60, repeats: true) { _ in
            Task { @MainActor in UpdateChecker.shared.checkIfDue() }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 10) { self.checkIfDue() }
    }

    func checkIfDue() {
        guard Settings.shared.checkForUpdates else { return }
        let last = UserDefaults.standard.object(forKey: lastCheckKey) as? Date ?? .distantPast
        if Date().timeIntervalSince(last) >= interval { Task { await check() } }
    }

    func check() async {
        status = "Checking…"
        onChange?()
        var request = URLRequest(url: Releases.latestAPI, timeoutInterval: 15)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("ShiftHappens/\(AppInfo.version)", forHTTPHeaderField: "User-Agent")
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard (response as? HTTPURLResponse)?.statusCode == 200, let found = Releases.decodeLatest(data) else {
                status = "Could not check for updates"
                onChange?()
                return
            }
            latest = found
            UserDefaults.standard.set(Date(), forKey: lastCheckKey)
            status = available.map { "Version \($0.version) is available" } ?? "You have the latest version"
        } catch {
            status = "Could not check for updates (offline?)"
        }
        onChange?()
    }
}

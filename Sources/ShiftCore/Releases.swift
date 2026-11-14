import Foundation

/// Release and issue links for this app's GitHub repo, and version comparison for the update notice.
public enum Releases {
    public static let repo = "Elaad-Adar/shift-happens"

    public static let latestAPI = URL(string: "https://api.github.com/repos/\(repo)/releases/latest")!
    public static let releasesPage = URL(string: "https://github.com/\(repo)/releases")!

    public static func notesURL(version: String) -> URL {
        URL(string: "https://github.com/\(repo)/releases/tag/v\(version)")!
    }

    /// "v1.10.2" or "1.10.2-beta" -> [1, 10, 2]. Nil when there is no leading number.
    public static func parse(_ version: String) -> [Int]? {
        var s = Substring(version.trimmingCharacters(in: .whitespaces))
        if s.first == "v" || s.first == "V" { s = s.dropFirst() }
        var parts: [Int] = []
        for piece in s.split(separator: ".", omittingEmptySubsequences: false) {
            let digits = piece.prefix { $0.isASCII && $0.isNumber }
            guard let n = Int(digits) else { break }
            parts.append(n)
            if digits.count != piece.count { break }  // "0-beta": stop after the number
        }
        return parts.isEmpty ? nil : parts
    }

    /// True when `candidate` is a higher version than `current` (missing parts count as 0).
    public static func isNewer(_ candidate: String, than current: String) -> Bool {
        guard let a = parse(candidate), let b = parse(current) else { return false }
        for i in 0..<max(a.count, b.count) {
            let x = i < a.count ? a[i] : 0
            let y = i < b.count ? b[i] : 0
            if x != y { return x > y }
        }
        return false
    }

    public struct Latest: Equatable {
        public let version: String  // without the leading "v"
        public let url: URL

        public init(version: String, url: URL) {
            self.version = version
            self.url = url
        }
    }

    /// The GitHub API answer for `releases/latest`: `tag_name` and `html_url`.
    public static func decodeLatest(_ data: Data) -> Latest? {
        struct Body: Decodable {
            let tag_name: String
            let html_url: String
        }
        guard let body = try? JSONDecoder().decode(Body.self, from: data),
              let url = URL(string: body.html_url), parse(body.tag_name) != nil else { return nil }
        let version = body.tag_name.hasPrefix("v") ? String(body.tag_name.dropFirst()) : body.tag_name
        return Latest(version: version, url: url)
    }

    public enum IssueKind: String {
        case bug
        case idea
    }

    /// New-issue page with the matching form (`.github/ISSUE_TEMPLATE/<kind>.yml`) and the app version
    /// and OS filled in (GitHub prefills form fields from query parameters named by field id).
    public static func issueURL(_ kind: IssueKind, version: String, os: String) -> URL {
        var c = URLComponents(string: "https://github.com/\(repo)/issues/new")!
        c.queryItems = [
            URLQueryItem(name: "template", value: "\(kind.rawValue).yml"),
            URLQueryItem(name: "version", value: version),
            URLQueryItem(name: "os", value: os),
        ]
        return c.url!
    }
}

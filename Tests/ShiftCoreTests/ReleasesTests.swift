import Foundation
import Testing
@testable import ShiftCore

@Suite struct ReleasesTests {
    @Test func parse() {
        #expect(Releases.parse("v1.2.0") == [1, 2, 0])
        #expect(Releases.parse("1.10") == [1, 10])
        #expect(Releases.parse("2.0.0-beta.1") == [2, 0, 0])
        #expect(Releases.parse("latest") == nil)
    }

    @Test func isNewer() {
        #expect(Releases.isNewer("v1.3.0", than: "1.2.0"))
        #expect(Releases.isNewer("1.10.0", than: "1.9.9"))
        #expect(Releases.isNewer("1.2.1", than: "1.2"))
        #expect(!Releases.isNewer("v1.2.0", than: "1.2.0"))
        #expect(!Releases.isNewer("1.2", than: "1.2.0"))
        #expect(!Releases.isNewer("1.1.9", than: "1.2.0"))
        #expect(!Releases.isNewer("junk", than: "1.2.0"))
    }

    @Test func decodeLatest() {
        let json = #"{"tag_name":"v1.3.0","html_url":"https://github.com/Elaad-Adar/shift-happens/releases/tag/v1.3.0","name":"x"}"#
        let latest = Releases.decodeLatest(Data(json.utf8))
        #expect(latest?.version == "1.3.0")
        #expect(latest?.url.absoluteString.hasSuffix("/releases/tag/v1.3.0") == true)
        #expect(Releases.decodeLatest(Data(#"{"message":"Not Found"}"#.utf8)) == nil)
    }

    @Test func issueURL() {
        let url = Releases.issueURL(.bug, version: "1.3.0", os: "macOS 15.6").absoluteString
        #expect(url.hasPrefix("https://github.com/Elaad-Adar/shift-happens/issues/new?"))
        #expect(url.contains("template=bug.yml"))
        #expect(url.contains("version=1.3.0"))
        #expect(url.contains("os=macOS%2015.6"))
        #expect(Releases.issueURL(.idea, version: "1", os: "x").absoluteString.contains("template=idea.yml"))
    }
}

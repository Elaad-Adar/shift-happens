import Foundation
import Testing
@testable import ShiftCore

@Suite struct LineCopyTests {
    /// Chromium stores custom MIME data as UTF-16LE strings in a pickle.
    func utf16(_ s: String) -> Data { s.data(using: .utf16LittleEndian)! }

    @Test func vsCodeEmptySelectionCopy() {
        let blob = Data([0x38, 0x01, 0, 0]) + utf16("vscode-editor-data")
            + utf16(#"{"version":1,"isFromEmptySelection":true,"isBlock":false}"#)
        #expect(LineCopy.isFromEmptySelection(blob))
    }

    @Test func realSelectionCopy() {
        let blob = utf16("vscode-editor-data") + utf16(#"{"version":1,"isFromEmptySelection":false}"#)
        #expect(!LineCopy.isFromEmptySelection(blob))
        #expect(!LineCopy.isFromEmptySelection(Data()))
    }
}

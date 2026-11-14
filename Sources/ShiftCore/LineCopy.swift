import Foundation

/// Code editors copy the whole current line when nothing is selected, which looks like a selection.
/// VS Code (and other Monaco/Electron editors) mark that copy in their custom clipboard data with
/// `"isFromEmptySelection":true`, stored as UTF-16 inside Chromium's web-custom-data blob
/// (macOS type `org.chromium.web-custom-data`, Windows format "Chromium Web Custom MIME Data Format").
public enum LineCopy {
    public static let macPasteboardType = "org.chromium.web-custom-data"
    public static let windowsClipboardFormat = "Chromium Web Custom MIME Data Format"

    private static let marker = Array(#""isFromEmptySelection":true"#.utf8)

    /// True when `data` (the web-custom-data blob) says the copy came from an empty selection.
    /// Zero bytes are dropped first, which turns the UTF-16LE ASCII text into plain ASCII.
    public static func isFromEmptySelection(_ data: Data) -> Bool {
        let ascii = data.filter { $0 != 0 }
        guard ascii.count >= marker.count else { return false }
        return ascii.range(of: Data(marker)) != nil
    }
}

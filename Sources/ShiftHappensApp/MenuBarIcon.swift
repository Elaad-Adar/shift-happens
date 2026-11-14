import AppKit

/// Menu-bar glyph matching the app icon: a keycap with "A" top-left and "ש" bottom-right.
/// Template image, so macOS tints it for light and dark menu bars.
enum MenuBarIcon {
    static func make() -> NSImage {
        let size = NSSize(width: 18, height: 18)
        let image = NSImage(size: size, flipped: false) { _ in
            let cap = NSBezierPath(roundedRect: NSRect(x: 1.5, y: 1.5, width: 15, height: 15), xRadius: 3.5, yRadius: 3.5)
            cap.lineWidth = 1.5
            NSColor.black.setStroke()
            cap.stroke()

            let font = NSFont.systemFont(ofSize: 7.5, weight: .bold)
            let attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: NSColor.black]
            NSAttributedString(string: "A", attributes: attrs).draw(at: NSPoint(x: 3.6, y: 7.6))
            let hebrew = NSAttributedString(string: "ש", attributes: [
                .font: NSFont(name: "ArialHebrew-Bold", size: 8.5) ?? font, .foregroundColor: NSColor.black,
            ])
            hebrew.draw(at: NSPoint(x: 8.2, y: 3.0))
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "Shift Happens"
        return image
    }
}

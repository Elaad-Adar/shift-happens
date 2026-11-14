// Builds the macOS app icon from the Gemini artwork.
//   swift scripts/make-icon.swift packaging/logo-gemini.jpeg build/AppIcon.iconset && iconutil -c icns build/AppIcon.iconset -o packaging/AppIcon.icns
// The source has its "transparent" checkerboard painted in, so the rounded square is found by colour
// (saturated pixels), cut out with a rounded-rect mask, and placed on a real transparent 1024 canvas
// at Apple's proportions (824 px body, 100 px margin) with a soft shadow.
import AppKit
import CoreGraphics

let args = CommandLine.arguments
guard args.count == 3, let src = NSImage(contentsOfFile: args[1]),
      let cg = src.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
    print("usage: make-icon.swift <source.png> <out.iconset>")
    exit(1)
}

let w = cg.width, h = cg.height
let space = CGColorSpaceCreateDeviceRGB()
var px = [UInt8](repeating: 0, count: w * h * 4)
let readCtx = CGContext(data: &px, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4, space: space,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
readCtx.draw(cg, in: CGRect(x: 0, y: 0, width: w, height: h))

// Row 0 of `px` is the top of the image.
func saturated(_ x: Int, _ y: Int) -> Bool {
    let i = (y * w + x) * 4
    let r = Int(px[i]), g = Int(px[i + 1]), b = Int(px[i + 2])
    return max(r, g, b) - min(r, g, b) > 70
}

var minX = w, minY = h, maxX = 0, maxY = 0
for y in 0..<h {
    for x in 0..<w where saturated(x, y) {
        minX = min(minX, x); maxX = max(maxX, x); minY = min(minY, y); maxY = max(maxY, y)
    }
}
let side = max(maxX - minX, maxY - minY)
// Corner radius from how far in along the diagonal the colour starts: inset = r (1 - 1/sqrt 2).
var t = 0
while t < side / 2 && !saturated(minX + t, minY + t) { t += 1 }
let radius = Double(t) / (1 - 1 / 2.0.squareRoot())
print("body \(minX),\(minY) to \(maxX),\(maxY), corner radius ~\(Int(radius))")

let inset = 3.0  // drop the anti-aliased rim, where checkerboard bleeds in
let crop = CGRect(x: Double(minX) + inset, y: Double(minY) + inset,
                  width: Double(maxX - minX) - 2 * inset, height: Double(maxY - minY) - 2 * inset)
let body = cg.cropping(to: crop)!

func render(_ size: Int) -> Data {
    let s = Double(size) / 1024
    let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0, space: space,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.interpolationQuality = .high
    let rect = CGRect(x: 100 * s, y: 100 * s, width: 824 * s, height: 824 * s)
    let r = (radius - inset) * 824 / crop.width * s
    let path = CGPath(roundedRect: rect, cornerWidth: r, cornerHeight: r, transform: nil)

    // Shadow: draw the shape once with a shadow, then the artwork clipped on top.
    ctx.saveGState()
    ctx.setShadow(offset: CGSize(width: 0, height: -10 * s), blur: 24 * s,
                  color: CGColor(gray: 0, alpha: 0.35))
    ctx.addPath(path)
    ctx.setFillColor(CGColor(red: 0.85, green: 0.25, blue: 0.35, alpha: 1))
    ctx.fillPath()
    ctx.restoreGState()

    ctx.addPath(path)
    ctx.clip()
    ctx.draw(body, in: rect)

    let rep = NSBitmapImageRep(cgImage: ctx.makeImage()!)
    return rep.representation(using: .png, properties: [:])!
}

let out = URL(fileURLWithPath: args[2])
try? FileManager.default.removeItem(at: out)
try FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
for base in [16, 32, 128, 256, 512] {
    try render(base).write(to: out.appendingPathComponent("icon_\(base)x\(base).png"))
    try render(base * 2).write(to: out.appendingPathComponent("icon_\(base)x\(base)@2x.png"))
}
print("wrote \(out.path)")

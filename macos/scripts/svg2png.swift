// Rasterise an SVG with transparency preserved (qlmanage fills the background white).
// Usage: svg2png <in.svg> <out.png> <size>
import AppKit
let a = CommandLine.arguments
guard a.count == 4, let size = Int(a[3]), let img = NSImage(contentsOfFile: a[1]) else {
    FileHandle.standardError.write("usage: svg2png in.svg out.png size\n".data(using: .utf8)!); exit(2)
}
let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8, samplesPerPixel: 4,
                           hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
NSGraphicsContext.current?.imageInterpolation = .high
img.draw(in: NSRect(x: 0, y: 0, width: size, height: size), from: .zero, operation: .sourceOver, fraction: 1)
NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: a[2]))

// Average linear luminance of the screen (0..1) from a ~50x30 capture. Requires the
// Screen Recording permission; only used when the content-aware preference is on.
// Only a single mean leaves this function (port of content.js).
import Foundation
import ScreenCaptureKit
import CoreGraphics

enum ScreenLuminance {
    private static func linear(_ v: Double) -> Double {
        let x = v / 255
        return x <= 0.04045 ? x / 12.92 : pow((x + 0.055) / 1.055, 2.4)
    }

    static func sample() async -> Double {
        guard #available(macOS 14.0, *) else { return -1 }
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            guard let display = content.displays.first(where: { CGDisplayIsBuiltin($0.displayID) != 0 }) ?? content.displays.first else { return -1 }
            let filter = SCContentFilter(display: display, excludingWindows: [])
            let cfg = SCStreamConfiguration()
            cfg.width = 50; cfg.height = 30
            cfg.showsCursor = false
            cfg.captureResolution = .nominal
            let img = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: cfg)
            return mean(of: img)
        } catch {
            return -1
        }
    }

    static func mean(of img: CGImage) -> Double {
        let w = img.width, h = img.height
        guard w > 0, h > 0 else { return -1 }
        var px = [UInt8](repeating: 0, count: w * h * 4)
        let cs = CGColorSpaceCreateDeviceRGB()
        guard let ctx = CGContext(data: &px, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4, space: cs,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return -1 }
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        var sum = 0.0
        for i in stride(from: 0, to: px.count, by: 4) {
            sum += 0.2126 * linear(Double(px[i])) + 0.7152 * linear(Double(px[i + 1])) + 0.0722 * linear(Double(px[i + 2]))
        }
        return sum / Double(w * h)
    }
}

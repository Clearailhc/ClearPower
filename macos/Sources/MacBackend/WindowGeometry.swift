import Foundation
import CoreGraphics

/// Window and screen geometry is always in logical points, never backing pixels.
public enum WindowGeometry {
    public static func contentSize(preferred: CGSize, visibleFrame: CGRect, margin: Double = 12) -> CGSize {
        CGSize(width: min(preferred.width, max(1, visibleFrame.width - 2 * margin)),
               height: min(preferred.height, max(1, visibleFrame.height - 2 * margin)))
    }

    public static func contained(_ frame: CGRect, in visibleFrame: CGRect) -> CGRect {
        let width = min(frame.width, visibleFrame.width)
        let height = min(frame.height, visibleFrame.height)
        return CGRect(x: min(max(frame.minX, visibleFrame.minX), visibleFrame.maxX - width),
                      y: min(max(frame.minY, visibleFrame.minY), visibleFrame.maxY - height),
                      width: width, height: height)
    }
}

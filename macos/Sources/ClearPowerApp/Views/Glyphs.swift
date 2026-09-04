// Tiny vector glyphs for Sankey nodes, drawn as paths so they follow the node colour and
// need no icon theme. Port of glyphs.js: 16 px box centred at (cx, cy).
import SwiftUI

enum Glyphs {
    struct Shape { var stroke = Path(); var fill = Path() }

    static func rr(_ p: inout Path, _ x: Double, _ y: Double, _ w: Double, _ h: Double, _ r: Double) {
        p.addRoundedRect(in: CGRect(x: x, y: y, width: w, height: h), cornerSize: CGSize(width: min(r, h / 2, w / 2), height: min(r, h / 2, w / 2)))
    }
    static func line(_ p: inout Path, _ x0: Double, _ y0: Double, _ x1: Double, _ y1: Double) {
        p.move(to: CGPoint(x: x0, y: y0)); p.addLine(to: CGPoint(x: x1, y: y1))
    }
    static func rect(_ p: inout Path, _ x: Double, _ y: Double, _ w: Double, _ h: Double) {
        p.addRect(CGRect(x: x, y: y, width: w, height: h))
    }
    static func circle(_ p: inout Path, _ cx: Double, _ cy: Double, _ r: Double) {
        p.addEllipse(in: CGRect(x: cx - r, y: cy - r, width: 2 * r, height: 2 * r))
    }

    static func shape(_ id: String, cx: Double, cy: Double) -> Shape {
        var s = Shape()
        switch id {
        case "adapter":               // wall plug: two prongs, body, cord
            line(&s.stroke, cx - 3, cy - 7, cx - 3, cy - 3)
            line(&s.stroke, cx + 3, cy - 7, cx + 3, cy - 3)
            rr(&s.stroke, cx - 6, cy - 3, 12, 7, 2.5)
            line(&s.stroke, cx, cy + 4, cx, cy + 7.5)
        case "battery", "batchg":
            rr(&s.stroke, cx - 7.5, cy - 4, 13, 8, 1.5)
            rect(&s.fill, cx + 6, cy - 2, 2, 4)
            rr(&s.fill, cx - 5.5, cy - 2, 6.5, 4, 0.8)
        case "pc":                    // laptop
            rr(&s.stroke, cx - 6, cy - 5.5, 12, 8, 1.2)
            line(&s.stroke, cx - 8, cy + 5, cx + 8, cy + 5)
        case "cpu":                   // chip with pins
            rr(&s.stroke, cx - 5, cy - 5, 10, 10, 1.8)
            rect(&s.fill, cx - 2.2, cy - 2.2, 4.4, 4.4)
            for d in [-3.0, 0, 3] {
                line(&s.stroke, cx + d, cy - 7.5, cx + d, cy - 5)
                line(&s.stroke, cx + d, cy + 5, cx + d, cy + 7.5)
                line(&s.stroke, cx - 7.5, cy + d, cx - 5, cy + d)
                line(&s.stroke, cx + 5, cy + d, cx + 7.5, cy + d)
            }
        case "soc":                   // system-on-chip: package with four tiles
            rr(&s.stroke, cx - 6.5, cy - 6.5, 13, 13, 2.2)
            for (dx, dy) in [(-4.0, -4.0), (1, -4), (-4, 1), (1, 1)] { rect(&s.fill, cx + dx, cy + dy, 3, 3) }
        case "gpu":                   // chip with a triangle (graphics)
            rr(&s.stroke, cx - 6.5, cy - 6.5, 13, 13, 2.2)
            var tri = Path()
            tri.move(to: CGPoint(x: cx, y: cy - 3.6)); tri.addLine(to: CGPoint(x: cx + 3.8, y: cy + 3)); tri.addLine(to: CGPoint(x: cx - 3.8, y: cy + 3)); tri.closeSubpath()
            s.fill.addPath(tri)
            for d in [-3.0, 3] {
                line(&s.stroke, cx + d, cy - 8, cx + d, cy - 6.5); line(&s.stroke, cx + d, cy + 6.5, cx + d, cy + 8)
                line(&s.stroke, cx - 8, cy + d, cx - 6.5, cy + d); line(&s.stroke, cx + 6.5, cy + d, cx + 8, cy + d)
            }
        case "mem":                   // RAM module: four chips on a stick with a notch
            rr(&s.stroke, cx - 8, cy - 4, 16, 8, 1)
            for d in [-6.5, -2.5, 1.5, 5.5] { rect(&s.fill, cx + d, cy - 2, 2.2, 3) }
            line(&s.stroke, cx - 8, cy + 4, cx - 1, cy + 4); line(&s.stroke, cx + 1, cy + 4, cx + 8, cy + 4)
            line(&s.stroke, cx - 1, cy + 4, cx - 1, cy + 5.5); line(&s.stroke, cx + 1, cy + 4, cx + 1, cy + 5.5)
        case "disp":                  // monitor
            rr(&s.stroke, cx - 7.5, cy - 6, 15, 10, 1.5)
            line(&s.stroke, cx, cy + 4, cx, cy + 6.5)
            line(&s.stroke, cx - 4, cy + 6.5, cx + 4, cy + 6.5)
        default:                      // ellipsis
            for d in [-5.0, 0, 5] { circle(&s.fill, cx + d, cy, 1.6) }
        }
        return s
    }

    static func draw(_ ctx: GraphicsContext, _ id: String, cx: Double, cy: Double, color: Color) {
        let s = shape(id, cx: cx, cy: cy)
        ctx.stroke(s.stroke, with: .color(color), style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round))
        ctx.fill(s.fill, with: .color(color))
    }
}

extension RGB {
    func color(_ alpha: Double = 1) -> Color { Color(red: r, green: g, blue: b, opacity: alpha) }
    func scaled(_ k: Double, alpha: Double = 1) -> Color { Color(red: min(r * k, 1), green: min(g * k, 1), blue: min(b * k, 1), opacity: alpha) }
}

func roundRectPath(_ x: Double, _ y: Double, _ w: Double, _ h: Double, _ r: Double) -> Path {
    let rr = min(r, h / 2, w / 2)
    return Path(roundedRect: CGRect(x: x, y: y, width: w, height: h), cornerRadius: rr)
}

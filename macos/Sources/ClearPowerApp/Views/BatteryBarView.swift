// Battery level bar with charge-limit marker and state glyph. Port of batteryBar.js.
import SwiftUI

struct BatteryBarView: View {
    let pct: Int
    let limit: Int
    let status: String
    let mode: String
    let onAc: Bool

    static let colors: [String: RGB] = [
        "charging": RGB(r: 0.40, g: 0.78, b: 0.52),
        "inhibited": RGB(r: 0.38, g: 0.70, b: 0.94),
        "discharging": RGB(r: 0.62, g: 0.65, b: 0.69),
        "forced": RGB(r: 0.95, g: 0.62, b: 0.32),
    ]

    var body: some View {
        Canvas { ctx, size in
            // Double throughout: the geometry helpers below take Double, and mixing it with
            // the CGFloat that Canvas hands back makes the arithmetic ambiguous.
            let w = Double(size.width), h = Double(size.height), r = h / 2
            let fg = Color(nsColor: .labelColor)
            ctx.fill(roundRectPath(0, 0, w, h, r), with: .color(Color(white: 0.5, opacity: 0.18)))

            var kind = "discharging", glyph = "–"
            if mode == "discharge" { kind = "forced"; glyph = "⤓" }
            else if status == "Charging" { kind = "charging"; glyph = "⚡" }
            else if onAc { kind = "inhibited"; glyph = "⏸" }
            let c = Self.colors[kind]!
            let fillW = max(h, w * Double(min(pct, 100)) / 100)
            let grad = Gradient(colors: [c.scaled(1.08, alpha: 0.92), c.scaled(0.88, alpha: 0.92)])
            ctx.fill(roundRectPath(0, 0, fillW, h, r), with: .linearGradient(grad, startPoint: .zero, endPoint: CGPoint(x: 0, y: h)))

            if limit < 100 {
                let x = (w * Double(limit) / 100).rounded() + 0.5
                var p = Path(); p.move(to: CGPoint(x: x, y: 5)); p.addLine(to: CGPoint(x: x, y: h - 5))
                ctx.stroke(p, with: .color(fg.opacity(0.7)), style: StrokeStyle(lineWidth: 1.5, dash: [2, 3]))
            }

            func paint(_ text: String, x: Double, y: Double) {
                let t = ctx.resolve(Text(text).font(.system(size: 13, weight: .bold)))
                let sz = t.measure(in: CGSize(width: 1000, height: 100))
                if x + Double(sz.width) <= fillW - 6 {
                    var shadow = ctx; shadow.opacity = 0.22
                    shadow.draw(ctx.resolve(Text(text).font(.system(size: 13, weight: .bold)).foregroundColor(.black)), at: CGPoint(x: x + 1, y: y + 1), anchor: .topLeading)
                    ctx.draw(ctx.resolve(Text(text).font(.system(size: 13, weight: .bold)).foregroundColor(.white.opacity(0.95))), at: CGPoint(x: x, y: y), anchor: .topLeading)
                } else {
                    ctx.draw(ctx.resolve(Text(text).font(.system(size: 13, weight: .bold)).foregroundColor(fg)), at: CGPoint(x: x, y: y), anchor: .topLeading)
                }
            }
            let pctText = "\(pct)%"
            let ps = ctx.resolve(Text(pctText).font(.system(size: 13, weight: .bold))).measure(in: CGSize(width: 1000, height: 100))
            paint(pctText, x: 12, y: (h - Double(ps.height)) / 2)
            let gs = ctx.resolve(Text(glyph).font(.system(size: 13, weight: .bold))).measure(in: CGSize(width: 1000, height: 100))
            paint(glyph, x: (w - Double(gs.width)) / 2, y: (h - Double(gs.height)) / 2)
        }
        .frame(height: 28)
    }
}

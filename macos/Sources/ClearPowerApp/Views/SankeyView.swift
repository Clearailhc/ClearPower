// Power-flow diagram. Port of the drawing half of sankey.js onto SwiftUI Canvas, plus
// hover tooltips (name · watts · what the node contains).
import SwiftUI
import ClearPowerCore

struct SankeyView: View {
    @ObservedObject var model: SankeyModel
    @State private var hovered: String? = nil

    var body: some View {
        GeometryReader { geo in
            let W = geo.size.width, H = geo.size.height
            let graph = layoutGraph(W: W, H: H)
            ZStack(alignment: .topLeading) {
                Canvas(rendersAsynchronously: false) { ctx, size in
                    if let g = graph { draw(ctx, size, g) }
                }
                if let g = graph {
                    ForEach(g.order, id: \.self) { id in
                        let n = g.nodes[id]!
                        Color.clear
                            .contentShape(Rectangle())
                            .frame(width: n.wPx, height: n.h)
                            .offset(x: n.x, y: n.y)
                            .onHover { inside in hovered = inside ? id : (hovered == id ? nil : hovered) }
                    }
                    if let id = hovered, let n = g.nodes[id] {
                        tooltip(for: n, W: W, H: H)
                    }
                }
            }
        }
        .frame(height: model.height)
    }

    private func layoutGraph(W: Double, H: Double) -> SankeyGraph? {
        guard let s = model.shown, W >= 100 else { return nil }
        var g = model.model(s)
        model.layout(&g, W: W, H: H)
        return g
    }

    private func tooltip(for n: SankeyNode, W: Double, H: Double) -> some View {
        let title = "\(n.label) · \((n.approx ? "≈" : "") + fmtW(n.w))"
        let desc = I18n.t(nodeTipKey(n))
        let bubbleW = 220.0
        // Prefer above the node; fall back below when there is no room.
        let above = n.y > 60
        let x = min(max(n.x + n.wPx / 2 - bubbleW / 2, 4), W - bubbleW - 4)
        return VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.system(size: 12, weight: .semibold))
            Text(desc).font(.system(size: 11)).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .padding(EdgeInsets(top: 6, leading: 9, bottom: 6, trailing: 9))
        .frame(width: bubbleW, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color(nsColor: .windowBackgroundColor)).shadow(radius: 6, y: 2))
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color(white: 0.5, opacity: 0.35), lineWidth: 1))
        .offset(x: x, y: above ? max(n.y - 58, 0) : min(n.y + n.h + 6, H - 50))
        .allowsHitTesting(false)
        .transition(.opacity)
    }

    private func bandPath(x0: Double, y0: Double, x1: Double, y1: Double, t: Double) -> Path {
        let mx = (x0 + x1) / 2
        var p = Path()
        p.move(to: CGPoint(x: x0, y: y0))
        p.addCurve(to: CGPoint(x: x1, y: y1), control1: CGPoint(x: mx, y: y0), control2: CGPoint(x: mx, y: y1))
        p.addLine(to: CGPoint(x: x1, y: y1 + t))
        p.addCurve(to: CGPoint(x: x0, y: y0 + t), control1: CGPoint(x: mx, y: y1 + t), control2: CGPoint(x: mx, y: y0 + t))
        p.closeSubpath()
        return p
    }

    private func draw(_ ctx: GraphicsContext, _ size: CGSize, _ g: SankeyGraph) {
        let W = size.width, H = size.height
        let scale = g.scale
        let fg = Color(nsColor: .labelColor)

        // Bands. Seam-free recipe: (1) clip away the node cards so bands slide under their
        // rounded outlines, (2) paint every band opaque into one layer with a hair of overlap,
        // (3) composite the layer once at 32 % — no antialiased edges between neighbours.
        let sheen = model.sheenEnabled()
        let R = 12.0, EXT = 10.0, OVERLAP = 0.35
        var clip = Path(CGRect(x: 0, y: 0, width: W, height: H))
        for id in g.order { let n = g.nodes[id]!; clip.addPath(roundRectPath(n.x, n.y, n.wPx, n.h, R)) }
        var bandCtx = ctx
        bandCtx.clip(to: clip, style: FillStyle(eoFill: true))
        bandCtx.opacity = 0.32
        // Band anchors advance per flow; work on copies so the layout stays reusable.
        var outOff: [String: Double] = [:], inOff: [String: Double] = [:]
        for id in g.order { outOff[id] = g.nodes[id]!.outOff; inOff[id] = g.nodes[id]!.inOff }
        bandCtx.drawLayer { layer in
            for f in g.flows {
                let a = g.nodes[f.a]!, b = g.nodes[f.b]!
                let t = max(f.w * scale, 2)
                let x0 = a.x + a.wPx - EXT, y0 = a.y + outOff[f.a]! - OVERLAP
                let x1 = b.x + EXT, y1 = b.y + inOff[f.b]! - OVERLAP
                let th = t + 2 * OVERLAP
                outOff[f.a]! += t
                inOff[f.b]! += t
                let path = bandPath(x0: x0, y0: y0, x1: x1, y1: y1, t: th)
                let grad = Gradient(colors: [a.color.color(), b.color.color()])
                layer.fill(path, with: .linearGradient(grad, startPoint: CGPoint(x: x0, y: 0), endPoint: CGPoint(x: x1, y: 0)))
                if sheen {
                    let pos = -0.3 + model.phase * 1.6
                    let half = 0.3
                    func tri(_ x: Double) -> Double { max(0, 1 - abs(x - pos) / half) }
                    let stops = [0, pos - half, pos, pos + half, 1].map { min(1, max(0, $0)) }.sorted()
                    let sg = Gradient(stops: stops.map { Gradient.Stop(color: Color.white.opacity(0.5 * tri($0)), location: $0) })
                    layer.fill(path, with: .linearGradient(sg, startPoint: CGPoint(x: x0, y: 0), endPoint: CGPoint(x: x1, y: 0)))
                }
            }
        }

        // Nodes: translucent card, glyph, small name, bold watts (drop the name, then the
        // glyph, when the card is too short).
        for id in g.order {
            let n = g.nodes[id]!
            let card = roundRectPath(n.x, n.y, n.wPx, n.h, 12)
            ctx.fill(card, with: .color(n.color.color(hovered == id ? 0.30 : 0.18)))
            ctx.stroke(card, with: .color(n.color.color(0.55)), lineWidth: 1)
            let watts = ctx.resolve(Text((n.approx ? "≈" : "") + fmtW(n.w)).font(.system(size: 12, weight: .bold)).foregroundColor(fg))
            let ws = watts.measure(in: CGSize(width: n.wPx - 6, height: 100))
            let name = ctx.resolve(Text(n.label).font(.system(size: 10)).foregroundColor(fg.opacity(0.75)))
            let ns = name.measure(in: CGSize(width: n.wPx - 8, height: 100))
            let glyphId = n.id == "batchg" ? "battery" : n.id
            let GLYPH_H = 18.0
            let cx = n.x + n.wPx / 2
            // The node geometry is Double; measure() returns CGFloat. Convert once, so the
            // layout arithmetic below stays unambiguous (and cheap to type-check).
            let nsH = Double(ns.height), wsH = Double(ws.height)
            if n.h >= GLYPH_H + nsH + wsH + 6 {
                let top = n.y + (n.h - GLYPH_H - nsH - wsH - 1) / 2
                Glyphs.draw(ctx, glyphId, cx: cx, cy: top + GLYPH_H / 2, color: n.color.color())
                ctx.draw(name, at: CGPoint(x: cx, y: top + GLYPH_H), anchor: .top)
                ctx.draw(watts, at: CGPoint(x: cx, y: top + GLYPH_H + nsH - 1), anchor: .top)
            } else if n.h >= GLYPH_H + wsH + 6 {
                let top = n.y + (n.h - GLYPH_H - wsH - 2) / 2
                Glyphs.draw(ctx, glyphId, cx: cx, cy: top + GLYPH_H / 2, color: n.color.color())
                ctx.draw(watts, at: CGPoint(x: cx, y: top + GLYPH_H + 2), anchor: .top)
            } else {
                ctx.draw(watts, at: CGPoint(x: cx, y: n.y + n.h / 2), anchor: .center)
            }
        }
    }
}

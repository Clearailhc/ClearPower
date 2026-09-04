// Sankey data model + easing, ported from the top half of sankey.js (everything above _draw).
import Foundation
import Combine
import ClearPowerCore
import MacBackend

struct RGB { let r: Double, g: Double, b: Double }

enum SankeyPalette {
    static let adapter = RGB(r: 0.36, g: 0.60, b: 0.92)
    static let battery = RGB(r: 0.40, g: 0.78, b: 0.52)
    static let pc = RGB(r: 0.58, g: 0.62, b: 0.68)
    static let cpu = RGB(r: 0.60, g: 0.48, b: 0.92)
    static let gpu = RGB(r: 0.86, g: 0.45, b: 0.78)
    static let soc = RGB(r: 0.45, g: 0.55, b: 0.95)
    static let mem = RGB(r: 0.55, g: 0.62, b: 0.78)
    static let disp = RGB(r: 0.92, g: 0.66, b: 0.34)
    static let other = RGB(r: 0.30, g: 0.74, b: 0.72)
}

struct Sink {
    let id: String
    var key: String
    var label: String
    let color: RGB
    var approx = false
    var tip = ""
}

/// Tooltip description key for a node.
func nodeTipKey(_ n: SankeyNode) -> String {
    switch n.id {
    case "adapter": return "tipAdapter"
    case "battery": return "tipBattery"
    case "batchg": return "tipBatchg"
    case "pc": return "tipSystem"
    case "other": return n.labelKey == "displayOther" ? "tipDisplayOther" : "tipOther"
    case "disp": return "tipDisplay"
    case "mem": return "tipMemory"
    case "cpu": return "tipCpu"
    case "gpu": return "tipGpu"
    case "soc": return "tipSoc"
    default: return n.labelKey == "system" ? "tipSystem" : "tipOther"
    }
}

let SINKS: [Sink] = [
    Sink(id: "cpu", key: "cpu_w", label: "cpu", color: SankeyPalette.cpu),
    Sink(id: "gpu", key: "gpu_w", label: "gpu", color: SankeyPalette.gpu),
    Sink(id: "soc", key: "soc_w", label: "soc", color: SankeyPalette.soc),
    Sink(id: "mem", key: "mem_w", label: "memory", color: SankeyPalette.mem),
    Sink(id: "disp", key: "display_w", label: "display", color: SankeyPalette.disp, approx: true),
    Sink(id: "other", key: "other_w", label: "other", color: SankeyPalette.other),
]
let NUMERIC = ["sys_w", "bat_w", "adapter_w"] + SINKS.map { $0.key }

let FPS = 15.0            // upper bound while the popover is open
let LERP = 0.18           // per-frame easing towards the latest sample
let SHEEN_SPEED = 0.28    // band sheen cycles per second
let MIN_SINK_W = 0.1      // sinks below this are folded into "other" and hidden
let NODE_H = 50.0, GAP = 10.0, PAD = 6.0

func fmtW(_ w: Double, digits: Int? = nil) -> String {
    if !w.isFinite || w < 0 { return "–" }
    let d = digits ?? (w >= 100 ? 0 : 1)
    return String(format: "%.\(d)f W", w)
}

final class SankeyNode {
    let id: String, label: String, w: Double, color: RGB
    var approx = false
    var labelKey = ""   // i18n key of the label, for the tooltip description
    var inTot = 0.0, outTot = 0.0, inOff = 0.0, outOff = 0.0
    var x = 0.0, y = 0.0, wPx = 0.0, h = 0.0
    init(id: String, label: String, w: Double, color: RGB) {
        self.id = id; self.label = label; self.w = w; self.color = color
    }
}

struct SankeyFlow { let a: String, b: String, w: Double }

struct SankeyGraph {
    var cols: [[SankeyNode]] = [[], [], []]
    var nodes: [String: SankeyNode] = [:]
    var order: [String] = []   // insertion order for stable drawing
    var flows: [SankeyFlow] = []
    var scale = 1.0
}

final class SankeyModel: ObservableObject {
    private(set) var target: [String: Any]? = nil
    @Published private(set) var shown: [String: Any]? = nil
    @Published private(set) var phase = 0.0
    @Published private(set) var height = 3 * NODE_H + 2 * GAP + 2 * PAD
    private var active = false
    private var timer: DispatchSourceTimer?
    private var lastTick = 0.0
    var flowMode = "on-ac" { didSet { if active { startTimer() }; objectWillChange.send() } }
    var reduceMotion: () -> Bool = { false }

    /// New data. Eases in while visible, snaps otherwise.
    func update(_ snap: [String: Any]) {
        if snap.d("cpu_w") < 0 { engineLogger.notice("sankey: cpu_w<0 pkg=\(snap.d("package_w")) sys=\(snap.d("sys_w")) src=\(snap.s("sys_source"), privacy: .public)") }
        target = snap
        if shown == nil {
            shown = snap
        } else {
            var s = shown!
            for (k, v) in snap {
                if !NUMERIC.contains(k) || (Snapshot.double(v) ?? -1) < 0 { s[k] = v }  // -1 snaps instantly
            }
            shown = s
        }
        fitHeight()
        if active {
            startTimer()
        } else {
            var s = shown!
            for k in NUMERIC { s[k] = snap[k] }
            shown = s
        }
    }

    /// Only animate while the popover is open: zero cost when closed.
    func setActive(_ a: Bool) {
        active = a
        if a { startTimer() } else { stopTimer() }
    }

    func sheenEnabled() -> Bool {
        if reduceMotion() || flowMode == "never" { return false }
        if flowMode == "on-ac" { return target?.b("on_ac") ?? false }
        return true
    }

    private func startTimer() {
        if timer != nil { return }
        lastTick = Date().timeIntervalSinceReferenceDate
        let t = DispatchSource.makeTimerSource(queue: .main)
        t.schedule(deadline: .now(), repeating: 1.0 / FPS)
        t.setEventHandler { [weak self] in self?.frame() }
        timer = t
        t.resume()
    }

    private func stopTimer() {
        timer?.cancel()
        timer = nil
    }

    private func frame() {
        let now = Date().timeIntervalSinceReferenceDate
        let dt = min(now - lastTick, 0.25)
        lastTick = now
        var moving = false
        if let tg = target, var s = shown {
            for k in NUMERIC {
                let tv = tg.d(k, 0)
                if tv < 0 { s[k] = tv; continue }
                let sv0 = s.d(k, tv)
                let sv = sv0 < 0 ? tv : sv0
                let n = sv + (tv - sv) * LERP
                if abs(tv - n) > 0.005 { s[k] = n; moving = true } else { s[k] = tv }
            }
            shown = s
        }
        let sheen = sheenEnabled()
        if sheen { phase = (phase + dt * SHEEN_SPEED).truncatingRemainder(dividingBy: 1) }
        if !sheen && !moving { stopTimer() }  // idle until the next sample
    }

    /// Which sinks are visible is decided on the *target* values so bands never flicker.
    func visibleSinks() -> [Sink] {
        guard let tg = target else { return [] }
        let measured = tg.d("cpu_w") >= 0
        let displayKnown = tg.d("display_w") >= 0
        if !measured {  // no per-block counters: everything we know is the total
            var s = SINKS[5]; s.key = "sys_w"; s.label = "system"; s.tip = "tipSystem"
            return [s]
        }
        var vis = SINKS.filter { s in
            if s.id == "disp" && !displayKnown { return false }
            return tg.d(s.key) >= MIN_SINK_W
        }
        if !vis.contains(where: { $0.id == "other" }) { vis.append(SINKS[5]) }
        return vis.map { s in
            if s.id == "other" && !displayKnown { var o = s; o.label = "displayOther"; return o }
            return s
        }
    }

    private func fitHeight() {
        let n = Double(max(3, visibleSinks().count))
        let h = n * NODE_H + (n - 1) * GAP + 2 * PAD
        if height != h { height = h }
    }

    func model(_ s: [String: Any]) -> SankeyGraph {
        let onAc = s.b("on_ac")
        let batW = s.d("bat_w", 0)
        let sysW = max(s.d("sys_w", 0), 0)
        var g = SankeyGraph()
        func add(_ col: Int, _ id: String, _ label: String, _ w: Double, _ color: RGB, key: String = "") -> SankeyNode {
            let n = SankeyNode(id: id, label: label, w: w, color: color)
            n.labelKey = key.isEmpty ? id : key
            g.nodes[id] = n; g.order.append(id); g.cols[col].append(n)
            return n
        }
        func flow(_ a: String, _ b: String, _ w: Double) {
            if w > 0.005 { g.flows.append(SankeyFlow(a: a, b: b, w: w)) }
        }
        if onAc {
            let fromBat = -batW >= 0.05 ? -batW : 0
            let toBat = batW >= 0.05 ? batW : 0
            let adToPc = max(sysW - fromBat, 0)
            _ = add(0, "adapter", I18n.t("adapter"), adToPc + toBat, SankeyPalette.adapter)
            if fromBat > 0 { _ = add(0, "battery", I18n.t("battery"), fromBat, SankeyPalette.battery) }
            if toBat > 0 { _ = add(1, "batchg", I18n.t("battery"), toBat, SankeyPalette.battery) }
            _ = add(1, "pc", I18n.t("system"), sysW, SankeyPalette.pc)
            flow("adapter", "batchg", toBat)
            flow("adapter", "pc", adToPc)
            flow("battery", "pc", fromBat)
        } else {
            _ = add(0, "battery", I18n.t("battery"), sysW, SankeyPalette.battery)
            _ = add(1, "pc", I18n.t("system"), sysW, SankeyPalette.pc)
            flow("battery", "pc", sysW)
        }
        // Sinks: eased values, hidden ones folded into "other", then normalised so that
        // they add up exactly to the eased total.
        let vis = visibleSinks()
        let visIds = Set(vis.map { $0.id })
        var hidden = 0.0
        for sk in SINKS where !visIds.contains(sk.id) && s.d(sk.key) > 0 && (target?.d("cpu_w") ?? -1) >= 0 {
            hidden += s.d(sk.key)
        }
        let vals = vis.map { max(s.d($0.key, 0), 0) + ($0.id == "other" ? hidden : 0) }
        let sum = vals.reduce(0, +)
        let k = (sum > 0.01 && sysW > 0) ? sysW / sum : 1
        for (i, v) in vis.enumerated() {
            let n = add(2, v.id, I18n.t(v.label), vals[i] * k, v.color, key: v.label)
            n.approx = v.approx
            flow("pc", v.id, vals[i] * k)
        }
        for f in g.flows {
            g.nodes[f.a]!.outTot += f.w
            g.nodes[f.b]!.inTot += f.w
        }
        return g
    }

    func layout(_ g: inout SankeyGraph, W: Double, H: Double) {
        let colW = [64.0, 64.0, 78.0]
        let colX = [PAD, (W / 2 - colW[1] / 2).rounded(), W - PAD - colW[2]]
        var scale = Double.infinity
        for col in g.cols {
            let tot = col.reduce(0) { $0 + $1.w }
            let avail = H - 2 * PAD - GAP * Double(col.count - 1)
            if tot > 0 { scale = min(scale, avail / tot) }
        }
        if !scale.isFinite { scale = 1 }
        for _ in 0..<6 {
            var ok = true
            for col in g.cols {
                let avail = H - 2 * PAD - GAP * Double(col.count - 1)
                let need = col.reduce(0) { $0 + max(NODE_H, $1.w * scale) }
                if need > avail + 0.5 { scale *= avail / need; ok = false }
            }
            if ok { break }
        }
        for (ci, col) in g.cols.enumerated() {
            let hs = col.map { max(NODE_H, $0.w * scale) }
            let total = hs.reduce(0, +) + GAP * Double(col.count - 1)
            var y = (H - total) / 2
            for (i, n) in col.enumerated() {
                n.x = colX[ci]; n.y = y; n.wPx = colW[ci]; n.h = hs[i]
                n.inOff = (n.h - n.inTot * scale) / 2
                n.outOff = (n.h - n.outTot * scale) / 2
                y += hs[i] + GAP
            }
        }
        g.scale = scale
    }
}

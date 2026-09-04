// Estimate per-application power from CPU share x (SoC power - idle floor).
// Port of daemon/clearpowerd/sources/procs.py minus psutil.
import Foundation

public struct ProcessBudget {
    public let interval: Double
    public let floorWindow: Double
    private var next = 0.0
    private var floor: [(t: Double, w: Double)] = []
    public private(set) var top: [(name: String, w: Double, cpuPct: Double)] = []

    public init(intervalS: Double = 3, floorWindowS: Double = 600) {
        interval = intervalS
        floorWindow = floorWindowS
    }

    private mutating func idleFloor(now: Double, packageW: Double) -> Double {
        floor.append((now, packageW))
        while let f = floor.first, now - f.t > floorWindow { floor.removeFirst() }
        return floor.map { $0.w }.min() ?? 0
    }

    public var due: Bool { true }

    /// `usage` is (process name, cpu percent) for every process with cpu > 0. Returns the
    /// top-n names with their share of the budget. Returns the previous result if called
    /// before `interval` has elapsed (pass `force` to bypass).
    public mutating func sample(now: Double, packageW: Double, usage: () -> [(String, Double)],
                                n: Int = 3, force: Bool = false) -> [(name: String, w: Double, cpuPct: Double)] {
        if now < next && !force { return top }
        next = now + interval
        let fl = packageW >= 0 ? idleFloor(now: now, packageW: packageW) : 0
        let budget = max(packageW - fl, 0)
        var agg: [String: Double] = [:]
        var total = 0.0
        for (name, c) in usage() where c > 0 {
            total += c
            agg[name.isEmpty ? "?" : name, default: 0] += c
        }
        var out: [(name: String, w: Double, cpuPct: Double)] = []
        if total > 0 {
            for (name, c) in agg.sorted(by: { $0.value > $1.value }).prefix(n) {
                out.append((name, budget * c / total, c))
            }
        }
        top = out
        return out
    }
}

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

    /// `usage` is (process name, cpu percent) for every process with cpu > 0, where keeping one
    /// core fully busy reports 100 (libproc rusage and the psutil/wall-clock convention agree).
    /// Returns the top-n names with their share of the budget. Returns the previous result if
    /// called before `interval` has elapsed (pass `force` to bypass).
    ///
    /// An unknown package power (-1: no SoC sensor, a counter stall, or the first sample after
    /// wake) must never reach the floor. The floor is a sliding minimum over ten minutes, so one
    /// bogus entry sticks for the whole window: recording -1 as 0 W makes every later budget the
    /// full package power, and recording a near-zero reading as the floor hides every
    /// application. An unusable sample is skipped outright, and the interval is left untouched so
    /// the next tick samples again instead of waiting out an interval for nothing.
    public mutating func sample(now: Double, packageW: Double, usage: () -> [(String, Double)],
                                n: Int = 3, force: Bool = false) -> [(name: String, w: Double, cpuPct: Double)] {
        if now < next && !force { return top }
        if packageW < 0 { return top }          // unknown power: leave both the floor and the interval alone
        next = now + interval
        let fl = idleFloor(now: now, packageW: packageW)
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

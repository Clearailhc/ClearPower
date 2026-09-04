// Battery runtime / time-to-limit estimates from the battery's own energy counter.
// Port of daemon/clearpowerd/runtime.py.
//
// A window of W seconds is evaluated over the current uninterrupted discharging (or
// charging) segment: avg_w = dE/dt. This integrates everything the machine drew and
// only moves as fast as the window.
import Foundation

public struct RuntimeEstimator {
    public static let windows = [600, 1800, 3600]
    public static let minBasisS = 60.0
    private static let capacity = 3700

    enum Phase { case dis, chg, idle }
    private var buf: [(t: Double, e: Double, phase: Phase)] = []

    public init() {}

    static func phase(_ status: String) -> Phase {
        switch status {
        case "Discharging": return .dis
        case "Charging": return .chg
        default: return .idle
        }
    }

    public mutating func add(t: Double, energyWh: Double, status: String) {
        buf.append((t, energyWh, Self.phase(status)))
        if buf.count > Self.capacity { buf.removeFirst(buf.count - Self.capacity) }
    }

    /// Oldest sample within window_s that shares the newest sample's phase, contiguously.
    private func oldestInSegment(_ windowS: Double) -> (Double, Double)? {
        guard let last = buf.last else { return nil }
        var oldest: (Double, Double)? = nil
        for s in buf.reversed() {
            if s.phase != last.phase || s.t < last.t - windowS { break }
            oldest = (s.t, s.e)
        }
        return oldest
    }

    /// Returns snapshot keys runtime_min_*, eta_min_*, runtime_basis_s (-1.0 = unknown).
    public func estimate(energyNowWh: Double, targetWh: Double, fallbackW: Double) -> [String: Any] {
        var out: [String: Any] = ["runtime_basis_s": 0]
        guard let last = buf.last else {
            for w in Self.windows {
                out["runtime_min_\(w / 60)"] = -1.0
                out["eta_min_\(w / 60)"] = -1.0
            }
            return out
        }
        let tNow = last.t
        let eNow = energyNowWh != 0 ? energyNowWh : last.e
        var basisOut = 0
        for w in Self.windows {
            let key = w / 60
            var runtime = -1.0, eta = -1.0
            var avgW: Double? = nil
            var basis = 0
            if let old = oldestInSegment(Double(w)) {
                let dt = tNow - old.0
                let de = old.1 - eNow  // positive when discharging
                if dt >= Self.minBasisS && abs(de) > 1e-3 {
                    avgW = de / dt * 3600.0
                    basis = Int(dt)
                }
            }
            switch last.phase {
            case .dis:
                var p: Double? = nil
                if let a = avgW, a > 0.3 { p = a } else if fallbackW > 0.3 { p = fallbackW }
                if let p = p { runtime = eNow / p * 60.0 }
            case .chg:
                var p: Double? = nil
                if let a = avgW, a < -0.3 { p = -a } else if fallbackW > 0.3 { p = fallbackW }
                if let p = p, targetWh > eNow { eta = (targetWh - eNow) / p * 60.0 }
            case .idle:
                break
            }
            out["runtime_min_\(key)"] = runtime
            out["eta_min_\(key)"] = eta
            let counts = (runtime > 0 || eta > 0) && avgW != nil
            basisOut = max(basisOut, counts ? basis : 0)
        }
        out["runtime_basis_s"] = basisOut
        return out
    }
}

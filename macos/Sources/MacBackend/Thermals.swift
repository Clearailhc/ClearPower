// Temperatures and fans from the SMC. Replaces daemon/clearpowerd/sources/hwmon.py.
// Apple Silicon exposes many die sensors (Tp* CPU clusters, Tg* GPU); we report the hottest.
import Foundation

public final class Thermals {
    private let cpuKeys: [String]
    private let gpuKeys: [String]
    private let fanKeys: [String]

    public init() {
        let keys = SMC.allKeys()
        cpuKeys = keys.filter { $0.hasPrefix("Tp") }
        gpuKeys = keys.filter { $0.hasPrefix("Tg") }
        let fans = Int(SMC.readFloat("FNum") ?? 0)
        fanKeys = (0..<max(fans, 0)).map { "F\($0)Ac" }.filter { keys.contains($0) }
    }

    private static func hottest(_ keys: [String]) -> Double {
        var best = -1.0
        for k in keys {
            if let v = SMC.readFloat(k), v > 0, v < 130 { best = max(best, v) }
        }
        return best
    }

    /// Snapshot keys temp_cpu, temp_gpu, temp_nvme, fan1, fan2 (-1 = unavailable). ~5 ms.
    public func read() -> [String: Any] {
        var out: [String: Any] = [
            "temp_cpu": Self.hottest(cpuKeys),
            "temp_gpu": Self.hottest(gpuKeys),
            "temp_nvme": -1.0,
            "fan1": -1, "fan2": -1,
        ]
        for (i, k) in fanKeys.prefix(2).enumerated() {
            if let rpm = SMC.readFloat(k) { out["fan\(i + 1)"] = Int(rpm.rounded()) }
        }
        return out
    }
}

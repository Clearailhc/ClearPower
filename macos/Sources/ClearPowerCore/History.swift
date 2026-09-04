// In-memory ring buffer history, downsampled (mean) to a fixed step.
// Port of daemon/clearpowerd/history.py.
import Foundation

public struct History {
    public static let fields = ["sys_w", "soc_w", "bat_w", "bat_pct", "display_w", "temp_cpu", "adapter_w"]
    public let step: Int
    private let capacity: Int
    private var buf: [String: [(t: Double, v: Double)]] = [:]
    private var acc: [String: (sum: Double, count: Int)] = [:]
    private var bucket: Int? = nil

    public init(seconds: Int = 86400, stepS: Int = 10) {
        step = stepS
        capacity = max(1, seconds / stepS)
        for f in Self.fields { buf[f] = []; acc[f] = (0, 0) }
    }

    public mutating func add(_ snap: [String: Any]) {
        guard let ts = Snapshot.double(snap["ts"]) else { return }
        let b = Int(ts) / step
        if bucket == nil { bucket = b }
        if b != bucket! {
            let t = Double(bucket! * step)
            for f in Self.fields {
                let (s, c) = acc[f]!
                if c > 0 {
                    buf[f]!.append((t, s / Double(c)))
                    if buf[f]!.count > capacity { buf[f]!.removeFirst(buf[f]!.count - capacity) }
                }
                acc[f] = (0, 0)
            }
            bucket = b
        }
        for f in Self.fields {
            if let v = Snapshot.double(snap[f]), v >= -1e9 {
                acc[f]!.sum += v
                acc[f]!.count += 1
            }
        }
    }

    public func get(_ field: String, seconds: Double) -> [(Double, Double)] {
        guard let arr = buf[field], let last = arr.last else { return [] }
        let t0 = last.t - seconds
        return arr.filter { $0.t >= t0 }.map { ($0.t, $0.v) }
    }
}

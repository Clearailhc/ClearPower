// The frontend/backend contract: a flat dictionary with the same keys and sentinels as
// the Linux daemon's D-Bus `Snapshot` (see daemon/data/org.clearpower.Daemon1.xml and
// the README). -1 means "unknown / unavailable" and must render as "–", never as zero.
// `bat_w` is positive into the battery.
import Foundation

public enum Snapshot {
    public static func double(_ v: Any?) -> Double? {
        switch v {
        case let d as Double: return d
        case let i as Int: return Double(i)
        case let f as Float: return Double(f)
        case let b as Bool: return b ? 1 : 0
        default: return nil
        }
    }

    public static func int(_ v: Any?) -> Int? {
        switch v {
        case let i as Int: return i
        case let d as Double: return d.isFinite ? Int(d) : nil
        case let b as Bool: return b ? 1 : 0
        default: return nil
        }
    }

    public static func bool(_ v: Any?) -> Bool? {
        switch v {
        case let b as Bool: return b
        case let i as Int: return i != 0
        default: return nil
        }
    }

    public static func string(_ v: Any?) -> String? { v as? String }

    /// JSON text for `--once` and debugging; keys sorted for stable diffs.
    public static func json(_ snap: [String: Any], pretty: Bool = true) -> String {
        var opts: JSONSerialization.WritingOptions = [.sortedKeys]
        if pretty { opts.insert(.prettyPrinted) }
        let clean = snap.mapValues { v -> Any in
            if let d = v as? Double, !d.isFinite { return -1.0 }
            return v
        }
        guard let data = try? JSONSerialization.data(withJSONObject: clean, options: opts),
              let s = String(data: data, encoding: .utf8) else { return "{}" }
        return s
    }
}

public extension Dictionary where Key == String, Value == Any {
    func d(_ key: String, _ fallback: Double = -1) -> Double { Snapshot.double(self[key]) ?? fallback }
    func i(_ key: String, _ fallback: Int = -1) -> Int { Snapshot.int(self[key]) ?? fallback }
    func b(_ key: String, _ fallback: Bool = false) -> Bool { Snapshot.bool(self[key]) ?? fallback }
    func s(_ key: String, _ fallback: String = "") -> String { Snapshot.string(self[key]) ?? fallback }
}

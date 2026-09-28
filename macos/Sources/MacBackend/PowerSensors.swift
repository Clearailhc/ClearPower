import Foundation

/// Model-specific SMC rail mappings. Keys are not interchangeable across SoCs.
/// M3 Max CPU mapping: https://github.com/vladkens/macmon/pull/77
/// Backlight mapping: https://github.com/exelban/stats/blob/master/Modules/Sensors/values.swift
public enum PowerSensors {
    public static let chip: String = {
        var size = 0
        guard sysctlbyname("machdep.cpu.brand_string", nil, &size, nil, 0) == 0, size > 0 else { return "" }
        var bytes = [CChar](repeating: 0, count: size)
        guard sysctlbyname("machdep.cpu.brand_string", &bytes, &size, nil, 0) == 0 else { return "" }
        return String(cString: bytes)
    }()

    static func nonnegative(_ value: Double?) -> Double? {
        guard let v = value, v.isFinite, v >= 0 else { return nil }
        return v
    }

    static func cpu(chip: String, read: (String) -> Double?) -> Double? {
        guard chip == "Apple M3 Max" else { return nil }
        var sum = 0.0
        for key in ["PC02", "PC03", "PC42", "PC43", "PP5b"] {
            guard let w = nonnegative(read(key)) else { return nil }
            sum += w
        }
        return sum
    }

    static func display(chip: String, read: (String) -> Double?) -> Double? {
        if chip == "Apple M3 Max" || chip.hasPrefix("Apple M1") || chip.hasPrefix("Apple M4") {
            return nonnegative(read("PDBR"))
        }
        if chip.hasPrefix("Apple M5") { return nonnegative(read("PBwo")) }
        return nil
    }
}

/// PMGR energy counters can remain frozen then jump by several minutes of energy.
/// Once stalling is detected, ignore that provider for this session, including its
/// delayed spikes. GPU Energy is provided independently by AGX and remains usable.
struct EnergyHealth {
    private var zeroSamples = 0
    private(set) var stalled = false

    mutating func usable(cpu: Double) -> Bool {
        if stalled { return false }
        guard cpu > 0, cpu.isFinite else {
            zeroSamples += 1
            if zeroSamples >= 2 { stalled = true }
            return false
        }
        zeroSamples = 0
        return true
    }
}

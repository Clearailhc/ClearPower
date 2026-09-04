// Per-block SoC energy from IOReport's "Energy Model" group (no root needed).
// Replaces daemon/clearpowerd/sources/rapl.py. Mapping onto the RAPL-shaped RawPower:
//   core   = CPU Energy          uncore = GPU Energy          dram = DRAM*
//   package = every top-level block: CPU + GPU + ANE + media + memory controllers + display
//             engines + PCIe, so soc = package - core - uncore is "everything else on the die".
import Foundation
import CSupport

public final class EnergySource {
    private var handle: OpaquePointer?
    private var buf = [cp_energy_entry](repeating: cp_energy_entry(), count: 512)
    public private(set) var lastChannels: [(String, Double)] = []   // watts, for debugging
    public private(set) var available = false
    public private(set) var lastSampleInfo: (channels: Int, elapsed: Double) = (0, 0)
    private var lastResult: (core: Double, uncore: Double, dram: Double, package: Double)? = nil

    public init() {
        handle = cp_ioreport_open("Energy Model")
        available = handle != nil
        if available { _ = sample() }  // prime
    }

    deinit { if let h = handle { cp_ioreport_close(h) } }

    /// Sub-channels that are already included in a total channel; never summed.
    private static func isSubChannel(_ n: String, hasCPUTotal: Bool, hasGPUTotal: Bool) -> Bool {
        if n.hasPrefix("EACC") || n.hasPrefix("PACC") || n.hasPrefix("ECPU") || n.hasPrefix("PCPU") { return true }
        if n.contains("_SRAM") || n.contains("DTL") || n.contains("CPM") { return true }
        if hasCPUTotal && n.hasSuffix("_CPU") { return true }
        if hasGPUTotal && n.hasPrefix("GPU") && n != "GPU Energy" { return true }
        return false
    }

    /// Returns (core, uncore, dram, package) in watts, or nil when no delta is available yet.
    public func sample() -> (core: Double, uncore: Double, dram: Double, package: Double)? {
        guard let h = handle else { return nil }
        var elapsed = 0.0
        let n = Int(cp_ioreport_sample(h, &buf, Int32(buf.count), &elapsed))
        lastSampleInfo = (n, elapsed)
        if n < 0 { return lastResult }   // sampled again within 50 ms: keep the previous reading
        guard n > 0 else { return nil }
        var entries: [(String, Double)] = []
        for i in 0..<n {
            let name = withUnsafeBytes(of: buf[i].name) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
            entries.append((name, buf[i].joules / elapsed))
        }
        lastChannels = entries
        let names = Set(entries.map { $0.0 })
        let hasCPUTotal = names.contains("CPU Energy")
        let hasGPUTotal = names.contains("GPU Energy")
        var core = 0.0, uncore = 0.0, dram = 0.0, package = 0.0
        var sawCPU = false, sawGPU = false, sawDRAM = false
        for (name, w) in entries {
            if Self.isSubChannel(name, hasCPUTotal: hasCPUTotal, hasGPUTotal: hasGPUTotal) { continue }
            if name == "CPU Energy" || (!hasCPUTotal && name.hasSuffix("_CPU")) { core += w; sawCPU = true }
            else if name == "GPU Energy" || (!hasGPUTotal && name.hasPrefix("GPU")) { uncore += w; sawGPU = true }
            else if name.hasPrefix("DRAM") { dram += w; sawDRAM = true }
            package += w
        }
        // package covers everything, dram included; the breakdown treats dram separately.
        package -= dram
        let result = (core: sawCPU ? core : -1, uncore: sawGPU ? uncore : -1, dram: sawDRAM ? dram : -1, package: package)
        lastResult = result
        return result
    }
}

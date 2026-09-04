// Smoothing + conserved power breakdown. Port of the arithmetic in
// daemon/clearpowerd/sampler.py (everything except the Linux sources).
//
// Every watt shown to the user is either measured (battery, platform total, per-block
// energy counters) or derived from measured values by subtraction, so the parts sum to
// the whole by construction. Inputs are smoothed (EMA) before the breakdown so the
// breakdown of smoothed values is still conserved. `raw` keeps the unsmoothed inputs
// for the runtime estimator and display calibration.
import Foundation

/// Raw, unsmoothed inputs. -1 = unavailable (except batW, which is signed and defaults to 0).
/// Domain names follow the Linux/RAPL shape so the maths is shared:
///   psys    whole-platform power (Linux: RAPL psys; macOS: SMC PSTR)
///   package SoC total           (Linux: RAPL package; macOS: sum of IOReport blocks)
///   core    CPU                 (Linux: RAPL core;    macOS: CPU Energy)
///   uncore  GPU                 (Linux: RAPL uncore;  macOS: GPU Energy)
///   dram    memory              (Linux: RAPL dram;    macOS: DRAM0)
/// soc = package - core - uncore (fabric, NPU, media, memory controller).
public struct RawPower {
    public var batW: Double = 0
    public var psys: Double = -1
    public var package: Double = -1
    public var core: Double = -1
    public var uncore: Double = -1
    public var dram: Double = -1
    public init() {}
    public init(batW: Double, psys: Double, package: Double, core: Double, uncore: Double, dram: Double) {
        self.batW = batW; self.psys = psys; self.package = package
        self.core = core; self.uncore = uncore; self.dram = dram
    }
    /// Platform power not attributable to the SoC or memory (display + peripherals).
    public var rest: Double {
        if psys < 0 { return -1 }
        return max(psys - max(package, 0) - max(dram, 0), 0)
    }
}

public struct PowerModel {
    static let smoothed = ["bat_w", "psys", "package", "core", "uncore", "dram"]
    private var ema: [String: Ema]
    public private(set) var raw = RawPower()

    public init(smoothingS: Double) {
        ema = Dictionary(uniqueKeysWithValues: Self.smoothed.map { ($0, Ema(tau: smoothingS)) })
    }

    private mutating func smooth(_ key: String, _ value: Double, _ now: Double) -> Double {
        if value < 0 {
            ema[key]!.reset()
            return -1
        }
        return ema[key]!.update(value, at: now)
    }

    /// Returns the breakdown keys of the snapshot. `displayEmission` is the calibrated panel
    /// emission for the current brightness (-1 when uncalibrated), `displayOn` whether the
    /// panel is lit.
    public mutating func update(raw input: RawPower, onAC: Bool, now: Double,
                                displayEmission: Double, displayOn: Bool) -> [String: Any] {
        raw = input
        // ---- smoothed inputs ----
        // bat_w is signed; the -1 sentinel logic must not apply to it.
        let batW = ema["bat_w"]!.update(input.batW, at: now)
        let psys = smooth("psys", input.psys, now)
        let package = smooth("package", input.package, now)
        let core = smooth("core", input.core, now)
        let uncore = smooth("uncore", input.uncore, now)
        let dram = smooth("dram", input.dram, now)

        // ---- whole-machine draw ----
        var sysW: Double, sysSource: String
        if !onAC && batW < -0.05 {
            sysW = -batW; sysSource = "battery"      // physical truth incl. all losses
        } else if psys > 0 {
            sysW = psys; sysSource = "psys"
        } else if package > 0 {
            sysW = package + 3.0; sysSource = "estimate"
        } else {
            sysW = -1; sysSource = "none"
        }

        // ---- breakdown (all derived by subtraction => conserved) ----
        var cpuW = -1.0, gpuW = -1.0, socW = -1.0, memW = -1.0, restW = -1.0
        if package > 0 && sysW > 0 {
            cpuW = max(core, 0)
            gpuW = max(uncore, 0)
            socW = max(package - cpuW - gpuW, 0)
            memW = max(dram, 0)
            var measured = package + memW
            if measured > sysW {  // platform total occasionally undershoots; keep it authoritative
                let k = sysW / measured
                cpuW *= k; gpuW *= k; socW *= k; memW *= k
                measured = sysW
            }
            restW = sysW - measured
        } else if sysW > 0 {
            restW = sysW
        }

        var displayW = -1.0
        var otherW = restW
        if restW >= 0 && displayEmission >= 0 {
            displayW = displayOn ? min(displayEmission, restW) : 0
            otherW = restW - displayW
        }

        // Adapter supplies the machine (minus whatever the battery is contributing when the
        // adapter is too weak) plus the charge current. Battery draw on AC => two sources.
        var adapterW = (onAC && sysW > 0) ? (sysW - max(-batW, 0) + max(batW, 0)) : 0
        adapterW = max(adapterW, 0)

        return [
            "sys_w": sysW, "sys_source": sysSource,
            "psys_w": psys, "package_w": package,
            "cpu_w": cpuW, "gpu_w": gpuW, "soc_w": socW, "mem_w": memW,
            "rest_w": restW, "display_w": displayW, "other_w": otherW,
            "bat_w": batW, "bat_w_raw": input.batW,
            "adapter_w": adapterW,
        ]
    }
}

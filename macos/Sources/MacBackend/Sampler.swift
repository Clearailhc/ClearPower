// Collect all macOS sources into one snapshot dict. Mirrors daemon/clearpowerd/sampler.py:
// the arithmetic lives in ClearPowerCore.PowerModel; this file only gathers inputs.
import Foundation
import ClearPowerCore

public final class Sampler {
    public let battery = BatterySource()
    public let energy = EnergySource()
    public let brightness = Brightness()
    public let thermals = Thermals()
    private var model: PowerModel
    public let displayCal: DisplayCalibration?
    private var thermal: [String: Any] = ["temp_cpu": -1.0, "temp_gpu": -1.0, "temp_nvme": -1.0, "fan1": -1, "fan2": -1]
    private var thermalAt = -1e9
    private var profile: (String, Bool) = ("", false)
    private var profileAt = -1e9
    public private(set) var last: [String: Any] = [:]
    public var raw: RawPower { model.raw }
    public var energyAvailable: Bool { energy.available }
    public var log: (String) -> Void = { _ in }

    public init(smoothingS: Double, displayCal: DisplayCalibration?) {
        model = PowerModel(smoothingS: smoothingS)
        self.displayCal = displayCal
    }

    /// Temps/fans cost a few ms of SMC reads; only while someone is looking, every 3 s.
    private func thermalRead(hot: Bool, now: Double) -> [String: Any] {
        if hot && now - thermalAt >= 3 {
            thermal = thermals.read()
            thermalAt = now
        }
        return thermal
    }

    private let profileQueue = DispatchQueue(label: "org.clearpower.powermode", qos: .utility)
    private var profileBusy = false

    /// pmset is a subprocess; never wait for it on the sampling thread (Process.waitUntilExit
    /// pumps the run loop and would re-enter the sampler). Refresh in the background and
    /// return the cached value.
    private func profileRead(hot: Bool, now: Double) -> (String, Bool) {
        if hot && now - profileAt >= 10 && !profileBusy {
            profileBusy = true
            profileAt = now
            profileQueue.async { [weak self] in
                let p = PowerMode.read()
                DispatchQueue.main.async {
                    self?.profile = p
                    self?.profileBusy = false
                }
            }
        }
        return profile
    }

    public func sample(hot: Bool = true) -> [String: Any] {
        let now = monotonicNow()
        var snap: [String: Any] = ["ts": Date().timeIntervalSince1970]
        let bat = battery.read()
        let bl = brightness.read()
        let plat = PlatformPower.read()
        let e = energy.sample()
        if e == nil { log("energy: no delta (elapsed too short or no channels), hot=\(hot) channels=\(energy.lastSampleInfo.channels) elapsed=\(energy.lastSampleInfo.elapsed)") }
        snap.merge(bat) { $1 }
        snap.merge(bl) { $1 }
        snap.merge(thermalRead(hot: hot, now: now)) { $1 }
        let onAC = bat.b("on_ac", true)

        var raw = RawPower()
        raw.batW = bat.d("bat_w", 0)
        raw.psys = plat.systemW
        if let e = e {
            raw.core = e.core; raw.uncore = e.uncore; raw.dram = e.dram; raw.package = e.package
        }
        let emission = displayCal?.emissionW(rawBrightness: bl["brightness_raw"] as? Int, now: now) ?? -1
        let breakdown = model.update(raw: raw, onAC: onAC, now: now,
                                     displayEmission: emission, displayOn: bl.b("display_on", true))
        snap.merge(breakdown) { $1 }
        // The SMC total is the same physical quantity as RAPL psys but a different sensor.
        if snap.s("sys_source") == "psys" { snap["sys_source"] = "smc" }
        snap["dc_in_w"] = plat.dcInW
        let (prof, high) = profileRead(hot: hot, now: now)
        snap["platform_profile"] = prof
        snap["high_power_supported"] = high
        snap["rapl_available"] = energy.available && e != nil
        if let dc = displayCal { snap.merge(dc.snapshotKeys) { $1 } }
        last = snap
        return snap
    }
}

/// Monotonic seconds (CLOCK_MONOTONIC), the `time.monotonic()` of the Python daemon.
public func monotonicNow() -> Double {
    var ts = timespec()
    clock_gettime(CLOCK_MONOTONIC, &ts)
    return Double(ts.tv_sec) + Double(ts.tv_nsec) / 1e9
}

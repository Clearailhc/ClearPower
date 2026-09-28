// Battery readings from IOKit `AppleSmartBattery` plus live power from the SMC.
// Replaces daemon/clearpowerd/sources/battery.py and usbpd.py.
import Foundation
import IOKit

public final class BatterySource {
    private var service: io_service_t = 0
    /// Nominal cell voltage used to turn mAh into Wh. Cancels out in runtime = E / (dE/dt),
    /// and gives Apple's advertised Wh (e.g. 6249 mAh x 3 cells x 3.86 V = 72.4 Wh).
    static let nominalCellV = 3.86

    public init() {
        service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSmartBattery"))
    }

    deinit { if service != 0 { IOObjectRelease(service) } }

    public var present: Bool { service != 0 }

    private func properties() -> [String: Any] {
        guard service != 0 else { return [:] }
        var props: Unmanaged<CFMutableDictionary>?
        guard IORegistryEntryCreateCFProperties(service, &props, kCFAllocatorDefault, 0) == KERN_SUCCESS,
              let dict = props?.takeRetainedValue() as? [String: Any] else { return [:] }
        return dict
    }

    static func num(_ v: Any?) -> Double? {
        if let n = v as? NSNumber {
            let value = n.doubleValue
            return value.isFinite ? value : nil
        }
        return nil
    }

    /// Snapshot keys: bat_*, cycle_count, on_ac, adapter_*.
    public func read() -> [String: Any] {
        Self.decode(properties(), smc: SMC.readFloat)
    }

    // Kept separate from IOKit so recorded telemetry can exercise OS schema changes.
    static func decode(_ p: [String: Any], smc: (String) -> Double?) -> [String: Any] {
        guard !p.isEmpty, (p["BatteryInstalled"] as? Bool) ?? true else { return ["bat_present": false, "on_ac": true] }
        let pct = Int(Self.num(p["CurrentCapacity"]) ?? 0)
        let data = p["BatteryData"] as? [String: Any] ?? [:]
        let rawNow = Self.num(p["AppleRawCurrentCapacity"]) ?? Self.num(data["RemainingCapacity"]) ?? -1
        let rawMax = Self.num(p["AppleRawMaxCapacity"]) ?? Self.num(data["FullChargeCapacity"]) ?? -1
        let design = Self.num(p["DesignCapacity"]) ?? Self.num(data["DesignCapacity"]) ?? -1
        let voltageMv = Self.num(p["Voltage"]) ?? 0
        let cells = ((p["BatteryData"] as? [String: Any])?["CellVoltage"] as? [Any])?.count ?? 3
        let vnom = Self.nominalCellV * Double(max(cells, 1))
        let external = (p["ExternalConnected"] as? Bool) ?? false
        let isCharging = (p["IsCharging"] as? Bool) ?? false
        let full = (p["FullyCharged"] as? Bool) ?? false
        // PPBR is not reliably battery power on macOS 27 (0.55 W while charging
        // at 35 W on M3 Max). Use signed current × voltage; never prefer PPBR.
        // IORegistry sometimes serializes negative current as unsigned two's complement.
        let instantMa = (p["InstantAmperage"] as? NSNumber).map { Double($0.int64Value) }
        func valid(_ value: Double?, _ range: ClosedRange<Double>) -> Double? {
            guard let v = value, v.isFinite, range.contains(v) else { return nil }
            return v
        }
        let current = valid(smc("B0AC"), -50000...50000) ?? valid(instantMa, -50000...50000)
        let voltage = valid(smc("B0AV"), 1000...30000) ?? valid(voltageMv, 1000...30000)
        let currentMa = current ?? 0
        let powerKnown = current != nil && voltage != nil
        let batW = powerKnown ? currentMa * voltage! / 1e6 : 0

        let status: String
        if currentMa < -50 { status = "Discharging" }
        else if isCharging || currentMa > 50 { status = "Charging" }
        else if full || pct >= 100 { status = "Full" }
        else { status = "Not charging" }

        var out: [String: Any] = [
            "bat_present": true,
            "bat_status": status,
            "bat_pct": max(pct, 0),
            "bat_w": batW,
            "bat_power_available": powerKnown,
            "bat_power_source": powerKnown ? "current-voltage" : "none",
            "bat_energy_wh": rawNow >= 0 ? rawNow * vnom / 1000 : -1,
            "bat_full_wh": rawMax > 0 ? rawMax * vnom / 1000 : -1,
            "bat_design_wh": design > 0 ? design * vnom / 1000 : -1,
            "bat_v": (voltage ?? -1000) / 1000,
            "cycle_count": Int(Self.num(p["CycleCount"]) ?? 0),
            "bat_model": (p["DeviceName"] as? String) ?? "",
            "bat_manufacturer": (p["Manufacturer"] as? String) ?? "",
            "on_ac": external,
        ]
        if let t = Self.num(p["Temperature"]) { out["temp_bat"] = t / 100 }
        if let ad = p["AdapterDetails"] as? [String: Any] {
            out["adapter_max_w"] = external ? (Self.num(ad["Watts"]) ?? 0) : 0
            out["adapter_v"] = external ? (Self.num(ad["AdapterVoltage"]) ?? 0) / 1000 : 0
            out["adapter_desc"] = external ? ((ad["Description"] as? String) ?? "") : ""
        } else {
            out["adapter_max_w"] = 0.0; out["adapter_v"] = 0.0
        }
        let telemetry = p["PowerTelemetryData"] as? [String: Any] ?? [:]
        for (source, key) in [("SystemPowerIn", "telemetry_dc_in_w"), ("SystemLoad", "telemetry_sys_w")] {
            if let mw = valid(Self.num(telemetry[source]), 0...1000000) { out[key] = mw / 1000 }
        }
        return out
    }
}

/// Whole-platform power from the SMC: PSTR (system total), PDTR (DC in from the adapter).
public enum PlatformPower {
    public static var available: Bool { SMC.exists("PSTR") }
    public static func read() -> (systemW: Double, dcInW: Double) {
        (SMC.readFloat("PSTR") ?? -1, SMC.readFloat("PDTR") ?? -1)
    }
}

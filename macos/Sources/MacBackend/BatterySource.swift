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

    private static func num(_ v: Any?) -> Double? {
        if let n = v as? NSNumber { return n.doubleValue }
        return nil
    }

    /// Snapshot keys: bat_*, cycle_count, on_ac, adapter_*.
    public func read() -> [String: Any] {
        let p = properties()
        guard !p.isEmpty, (p["BatteryInstalled"] as? Bool) ?? true else { return ["bat_present": false, "on_ac": true] }
        let pct = Int(Self.num(p["CurrentCapacity"]) ?? 0)
        let rawNow = Self.num(p["AppleRawCurrentCapacity"]) ?? 0
        let rawMax = Self.num(p["AppleRawMaxCapacity"]) ?? 0
        let design = Self.num(p["DesignCapacity"]) ?? 0
        let voltageMv = Self.num(p["Voltage"]) ?? 0
        let cells = ((p["BatteryData"] as? [String: Any])?["CellVoltage"] as? [Any])?.count ?? 3
        let vnom = Self.nominalCellV * Double(max(cells, 1))
        let external = (p["ExternalConnected"] as? Bool) ?? false
        let isCharging = (p["IsCharging"] as? Bool) ?? false
        let full = (p["FullyCharged"] as? Bool) ?? false
        let instantMa = Self.num(p["InstantAmperage"]) ?? 0

        // Live power from the SMC (IOKit's telemetry refreshes only every few seconds).
        // Sign convention: positive = into the battery.
        let smcCurrentMa = SMC.readFloat("B0AC")
        let smcVoltageMv = SMC.readFloat("B0AV")
        let smcPowerW = SMC.readFloat("PPBR")
        let currentMa = smcCurrentMa ?? instantMa
        var batW: Double
        if let pw = smcPowerW {
            batW = abs(pw) * (currentMa < 0 ? -1 : 1)
        } else {
            batW = currentMa * (smcVoltageMv ?? voltageMv) / 1e6
        }
        if abs(currentMa) < 1 { batW = 0 }

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
            "bat_energy_wh": rawNow * vnom / 1000,
            "bat_full_wh": rawMax * vnom / 1000,
            "bat_design_wh": design * vnom / 1000,
            "bat_v": (smcVoltageMv ?? voltageMv) / 1000,
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

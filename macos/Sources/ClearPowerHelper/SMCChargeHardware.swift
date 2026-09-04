// Charge control on Apple Silicon through SMC keys. Three key generations are known
// (see macos/scripts/probe/README.md and charlie0129/batt):
//   firmware  bfF0/bfD0/bfE0  the firmware enforces lower/upper thresholds itself (macOS 27-era)
//   legacy    CH0B+CH0C 0x02 inhibit; CH0I (or CH0J) 0x01 disables the adapter
//   tahoe     CHTE ui32 LE 1 inhibit; CHIE 0x08 disables the adapter
// Without firmware thresholds the helper enforces the hysteresis band itself in `enforce`.
import Foundation
import ClearPowerCore
import MacBackend

final class SMCChargeHardware: ChargeHardware {
    enum Method: String { case firmware, legacy, tahoe, none }
    let method: Method
    private let adapterKey: String?   // CH0I / CH0J / CHIE
    private let stateURL: URL
    let log: (String) -> Void

    // Desired state (the state machine's view)
    private(set) var start = 95
    private(set) var end = 100
    private(set) var behaviour = "auto"
    // Applied state
    private(set) var chargingInhibited = false
    private(set) var adapterDisabled = false

    init(stateDirectory: String, log: @escaping (String) -> Void) {
        self.log = log
        stateURL = URL(fileURLWithPath: stateDirectory).appendingPathComponent("state.json")
        if SMC.exists("bfF0") && SMC.exists("bfD0") && SMC.exists("bfE0") {
            method = .firmware
        } else if SMC.exists("CH0B") && SMC.exists("CH0C") {
            method = .legacy
        } else if SMC.exists("CHTE") {
            method = .tahoe
        } else {
            method = .none
        }
        if SMC.exists("CH0I") { adapterKey = "CH0I" }
        else if SMC.exists("CH0J") { adapterKey = "CH0J" }
        else if SMC.exists("CHIE") { adapterKey = "CHIE" }
        else { adapterKey = nil }
        log("charge control method: \(method.rawValue), adapter key: \(adapterKey ?? "none")")
    }

    // ---- ChargeHardware ----
    var thresholdsSupported: Bool { method != .none }
    var behaviours: [String] {
        var b = ["auto", "inhibit-charge"]
        if adapterKey != nil && method != .none { b.append("force-discharge") }
        return b
    }

    func writeThresholds(start: Int, end: Int) throws {
        self.start = start
        self.end = end
        if method == .firmware {
            _ = try writeFirmwareLimit(lower: start, upper: end)
        }
        // Other methods: applied on the next `enforce`.
    }

    func writeBehaviour(_ b: String) throws {
        guard behaviours.contains(b) else { throw ChargeError(errno: 22, "charge_behaviour '\(b)' unsupported") }
        behaviour = b
        switch b {
        case "force-discharge":
            try setAdapter(enabled: false)
        case "inhibit-charge":
            try setAdapter(enabled: true)
            try setCharging(enabled: false)
        default:
            try setAdapter(enabled: true)
        }
    }

    func loadLimit() -> Int? {
        guard let data = try? Data(contentsOf: stateURL),
              let d = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        return Snapshot.int(d["limit"])
    }

    func saveLimit(_ limit: Int) {
        do {
            try FileManager.default.createDirectory(at: stateURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            try JSONSerialization.data(withJSONObject: ["limit": limit]).write(to: stateURL, options: .atomic)
        } catch {
            log("cannot save state: \(error)")
        }
    }

    // ---- enforcement (called periodically, on wake, and after every command) ----
    /// Apply the hysteresis band for the current battery level and re-assert the keys
    /// (firmware / PD renegotiation can silently reset them). Returns true if anything changed.
    @discardableResult
    func enforce(batPct: Int) -> Bool {
        var changed = false
        do {
            switch behaviour {
            case "force-discharge":
                changed = try setAdapter(enabled: false) || changed
            case "inhibit-charge":
                changed = try setAdapter(enabled: true) || changed
                changed = try setCharging(enabled: false) || changed
            default:
                changed = try setAdapter(enabled: true) || changed
                if method != .firmware {
                    if end >= 100 || batPct <= start {
                        changed = try setCharging(enabled: true) || changed
                    } else if batPct >= end {
                        changed = try setCharging(enabled: false) || changed
                    } else {
                        changed = try setCharging(enabled: !chargingInhibited) || changed  // hold, but re-assert
                    }
                } else {
                    changed = try writeFirmwareLimit(lower: start, upper: end) || changed
                }
            }
        } catch {
            log("enforce failed: \(error)")
        }
        return changed
    }

    /// Before sleep: nobody can enforce the limit while asleep, so stop charging once the
    /// battery is inside the hysteresis band (it would otherwise overshoot to 100 %).
    func prepareForSleep(batPct: Int) {
        guard behaviour == "auto", method != .firmware, end < 100, batPct >= start else { return }
        do {
            if try setCharging(enabled: false) { log("sleep: charging stopped at \(batPct)% (limit \(end))") }
        } catch { log("sleep prepare failed: \(error)") }
    }

    /// Restore platform defaults (helper exit).
    func reset() {
        behaviour = "auto"
        _ = try? setAdapter(enabled: true)
        _ = try? setCharging(enabled: true)
        if method == .firmware { _ = SMC.write("bfF0", [0x00]) }
    }

    // ---- SMC writes ----
    @discardableResult
    private func setCharging(enabled: Bool) throws -> Bool {
        var wrote = false
        switch method {
        case .legacy:
            let v: UInt8 = enabled ? 0x00 : 0x02
            for k in ["CH0B", "CH0C"] {
                if SMC.read(k)?.bytes.first != v { try write(k, [v]); wrote = true }
            }
        case .tahoe:
            let v: [UInt8] = enabled ? [0, 0, 0, 0] : [1, 0, 0, 0]
            if SMC.read("CHTE")?.bytes != v { try write("CHTE", v); wrote = true }
        case .firmware, .none:
            break
        }
        if wrote || chargingInhibited != !enabled {
            log("charging \(enabled ? "allowed" : "inhibited")")
        }
        chargingInhibited = !enabled
        return wrote
    }

    @discardableResult
    private func setAdapter(enabled: Bool) throws -> Bool {
        guard let key = adapterKey else {
            if !enabled { throw ChargeError(errno: 95, "force-discharge not supported") }
            return false
        }
        let v: UInt8 = enabled ? 0x00 : (key == "CHIE" ? 0x08 : 0x01)
        var wrote = false
        if SMC.read(key)?.bytes.first != v { try write(key, [v]); wrote = true }
        if wrote { log("adapter \(enabled ? "enabled" : "disabled") via \(key)") }
        adapterDisabled = !enabled
        return wrote
    }

    private func writeFirmwareLimit(lower: Int, upper: Int) throws -> Bool {
        func u32le(_ v: Int) -> [UInt8] { [UInt8(v & 0xff), UInt8((v >> 8) & 0xff), UInt8((v >> 16) & 0xff), UInt8((v >> 24) & 0xff)] }
        let active = SMC.read("bfF0")?.bytes.first == 0x02
        let curUpper = SMC.read("bfD0")?.bytes, curLower = SMC.read("bfE0")?.bytes
        if upper >= 100 {
            if active { try write("bfF0", [0x00]); return true }
            return false
        }
        if active && curUpper == u32le(upper) && curLower == u32le(lower) { return false }
        // Write order required by the firmware: deactivate, upper, lower, activate.
        try write("bfF0", [0x00])
        try write("bfD0", u32le(upper))
        try write("bfE0", u32le(lower))
        try write("bfF0", [0x02])
        return true
    }

    private func write(_ key: String, _ bytes: [UInt8]) throws {
        let r = SMC.write(key, bytes)
        if r != 0 {
            throw ChargeError(errno: r == -1 ? 2 : 13, "SMC write \(key) failed (\(r))")
        }
    }
}

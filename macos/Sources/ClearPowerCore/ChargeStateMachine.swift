// Charge control state machine: limit (normal) / topup / discharge.
// Port of the policy half of daemon/clearpowerd/charge_control.py; the hardware half
// is behind `ChargeHardware` so Linux thresholds and macOS SMC keys share one machine.
import Foundation

public enum ChargeMode: String { case limit, topup, discharge }

public struct ChargeError: Error, CustomStringConvertible {
    public let errno: Int32
    public let message: String
    public init(errno: Int32, _ message: String) { self.errno = errno; self.message = message }
    public var description: String { "\(message) (errno \(errno))" }
}

/// What the platform can do. Behaviours use the Linux `charge_behaviour` vocabulary:
/// "auto", "inhibit-charge", "force-discharge".
public protocol ChargeHardware: AnyObject {
    var thresholdsSupported: Bool { get }
    var behaviours: [String] { get }
    /// Charging stops at `end` and resumes below `start` (Linux writes them to sysfs; the
    /// macOS helper enforces them itself, or hands them to firmware where supported).
    func writeThresholds(start: Int, end: Int) throws
    func writeBehaviour(_ behaviour: String) throws
    /// Persisted limit (nil = none saved). Only the limit survives restarts.
    func loadLimit() -> Int?
    func saveLimit(_ limit: Int)
}

public final class ChargeStateMachine {
    public private(set) var mode: ChargeMode = .limit
    public private(set) var target = 0
    public private(set) var limit = 100
    public let floor: Int
    private let hw: ChargeHardware

    public var supported: Bool { hw.thresholdsSupported }
    public var dischargeSupported: Bool { hw.behaviours.contains("force-discharge") }

    public init(hardware: ChargeHardware, dischargeFloorPct: Int = 20) {
        hw = hardware
        floor = dischargeFloorPct
        limit = Self.clampLimit(hw.loadLimit() ?? 100)
    }

    static func clampLimit(_ v: Int) -> Int { max(50, min(100, v)) }

    private func applyLimit() throws {
        guard hw.thresholdsSupported else { return }
        let end = limit
        let start = end >= 100 ? 95 : end - 5
        try hw.writeThresholds(start: start, end: end)
    }

    // ---- public API --------------------------------------------------

    /// Make hardware consistent with saved state; special modes never survive restart.
    public func applyStartup() {
        guard hw.thresholdsSupported else { return }
        try? hw.writeBehaviour("auto")
        try? applyLimit()
    }

    public func shutdown() {
        if mode != .limit {
            try? hw.writeBehaviour("auto")
            try? applyLimit()
        }
    }

    public func setLimit(_ pct: Int) throws {
        let prev = limit
        limit = Self.clampLimit(pct)
        do {
            if mode == .limit { try applyLimit() }
        } catch {
            limit = prev  // keep state and hardware consistent
            throw error
        }
        if mode == .discharge && target < limit { target = limit }
        hw.saveLimit(limit)
    }

    public func startTopUp() throws {
        mode = .topup
        try hw.writeBehaviour("auto")
        try hw.writeThresholds(start: 95, end: 100)
    }

    public func startDischarge(target requested: Int) throws {
        guard dischargeSupported else { throw ChargeError(errno: 95, "force-discharge not supported") }
        let t = requested > 0 ? requested : limit
        target = max(floor, min(99, t))
        mode = .discharge
        try hw.writeBehaviour("force-discharge")
    }

    public func cancel() throws {
        mode = .limit
        target = 0
        try hw.writeBehaviour("auto")
        try applyLimit()
    }

    /// Called every sample; ends special modes when their goal is reached.
    /// Returns true when a special mode was ended.
    @discardableResult
    public func tick(batPct: Int, batStatus: String) -> Bool {
        guard mode != .limit else { return false }
        if mode == .topup && (batStatus == "Full" || batPct >= 100) {
            try? cancel(); return true
        }
        if mode == .discharge && batPct <= target {
            try? cancel(); return true
        }
        return false
    }

    public var state: [String: Any] {
        ["charge_mode": mode.rawValue, "charge_limit": limit, "charge_target": target]
    }
}

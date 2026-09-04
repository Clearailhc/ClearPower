// XPC contract between the menu bar app and the privileged helper. Mirrors the write side
// of the Linux D-Bus interface org.clearpower.Daemon1 (README "D-Bus API").
import Foundation

public let helperMachServiceName = "org.clearpower.helper"
public let helperLabel = "org.clearpower.helper"
public let helperInstallPath = "/Library/PrivilegedHelperTools/org.clearpower.helper"
public let helperPlistPath = "/Library/LaunchDaemons/org.clearpower.helper.plist"
public let helperStateDirectory = "/Library/Application Support/ClearPower"
public let helperLogPath = "/Library/Logs/ClearPower/helper.log"

/// Replies carry an error message (nil = success) so the UI can show "Not permitted" /
/// "Not supported" exactly like the GNOME frontend does with D-Bus errors.
@objc public protocol HelperProtocol {
    /// JSON object: charge_mode, charge_limit, charge_target, charge_behaviour,
    /// charge_start_threshold, charge_end_threshold, charging_inhibited, adapter_disabled,
    /// control_supported, discharge_supported, control_method, bat_pct, version.
    func getState(reply: @escaping (Data) -> Void)
    func setChargeLimit(_ percent: Int, reply: @escaping (String?) -> Void)
    func startTopUp(reply: @escaping (String?) -> Void)
    func startDischarge(_ targetPercent: Int, reply: @escaping (String?) -> Void)
    func cancelSpecial(reply: @escaping (String?) -> Void)
    /// 0 automatic, 1 low power, 2 high power (pmset powermode / lowpowermode).
    func setPowerMode(_ mode: Int, reply: @escaping (String?) -> Void)
    func version(reply: @escaping (String) -> Void)
}

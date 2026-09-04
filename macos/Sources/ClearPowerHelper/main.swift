// ClearPower privileged helper: a root launchd daemon that owns charge control and nothing
// else. It keeps working when the app is closed, restores charging on exit, and answers
// the app over XPC (org.clearpower.helper).
import Foundation
import IOKit
import IOKit.pwr_mgt
import ClearPowerCore
import ClearPowerIPC
import MacBackend

func log(_ s: String) {
    let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd HH:mm:ss"
    FileHandle.standardError.write("\(f.string(from: Date())) \(s)\n".data(using: .utf8)!)
}

final class HelperCore {
    let hw: SMCChargeHardware
    let charge: ChargeStateMachine
    let battery = BatterySource()
    private var timer: DispatchSourceTimer?
    private var currentInterval = 0
    private var lastPct = -1
    private var lastStatus = ""
    let queue = DispatchQueue(label: "org.clearpower.helper")

    init() {
        hw = SMCChargeHardware(stateDirectory: helperStateDirectory, log: log)
        charge = ChargeStateMachine(hardware: hw, dischargeFloorPct: 20)
        charge.applyStartup()
        log("started: version \(ClearPowerVersion.string), limit \(charge.limit), supported \(charge.supported), discharge \(charge.dischargeSupported)")
        schedule()
    }

    private func readBattery() {
        let b = battery.read()
        lastPct = b.i("bat_pct", -1)
        lastStatus = b.s("bat_status")
    }

    /// One control step: read the battery, end special modes, enforce the band.
    func step() {
        readBattery()
        guard lastPct >= 0 else { return }
        charge.tick(batPct: lastPct, batStatus: lastStatus)
        hw.enforce(batPct: lastPct)
    }

    /// 30 s normally; 5 s in a special mode or within 3 % of a threshold.
    private func interval() -> Int {
        if charge.mode != .limit { return 5 }
        if lastPct >= 0 && (abs(lastPct - hw.end) <= 3 || abs(lastPct - hw.start) <= 3) { return 5 }
        return 30
    }

    /// (Re)arm the control timer. Only rebuilt when the desired interval changes; the
    /// first run fires immediately, later ones after a full interval.
    private func schedule(immediately: Bool = false) {
        let want = interval()
        if timer != nil && want == currentInterval && !immediately { return }
        timer?.cancel()
        currentInterval = want
        let t = DispatchSource.makeTimerSource(queue: queue)
        t.schedule(deadline: .now() + .seconds(immediately || timer == nil ? 0 : want), repeating: .seconds(want), leeway: .seconds(1))
        t.setEventHandler { [weak self] in
            guard let self = self else { return }
            self.step()
            self.schedule()
        }
        timer = t
        t.resume()
    }

    func state() -> [String: Any] {
        var s = charge.state
        s["charge_behaviour"] = hw.behaviour
        s["charge_start_threshold"] = hw.start
        s["charge_end_threshold"] = hw.end
        s["charging_inhibited"] = hw.chargingInhibited
        s["adapter_disabled"] = hw.adapterDisabled
        s["control_supported"] = charge.supported
        s["discharge_supported"] = charge.dischargeSupported
        s["control_method"] = hw.method.rawValue
        s["bat_pct"] = lastPct
        s["version"] = ClearPowerVersion.string
        return s
    }

    func willSleep() {
        queue.sync {
            readBattery()
            hw.prepareForSleep(batPct: lastPct)
        }
    }

    func didWake() {
        queue.async { self.schedule(immediately: true) }
    }

    func shutdown() {
        queue.sync {
            log("shutting down: restoring charging")
            charge.shutdown()
            hw.reset()
        }
    }
}

// ---- XPC ----
final class HelperService: NSObject, HelperProtocol {
    let core: HelperCore
    init(core: HelperCore) { self.core = core }

    private func run(_ body: @escaping () throws -> Void, reply: @escaping (String?) -> Void) {
        core.queue.async {
            do {
                try body()
                self.core.step()
                reply(nil)
            } catch {
                reply("\(error)")
            }
        }
    }

    func getState(reply: @escaping (Data) -> Void) {
        core.queue.async {
            let data = (try? JSONSerialization.data(withJSONObject: self.core.state())) ?? Data("{}".utf8)
            reply(data)
        }
    }
    func setChargeLimit(_ percent: Int, reply: @escaping (String?) -> Void) {
        run({ try self.core.charge.setLimit(percent) }, reply: reply)
    }
    func startTopUp(reply: @escaping (String?) -> Void) {
        run({ try self.core.charge.startTopUp() }, reply: reply)
    }
    func startDischarge(_ targetPercent: Int, reply: @escaping (String?) -> Void) {
        run({ try self.core.charge.startDischarge(target: targetPercent) }, reply: reply)
    }
    func cancelSpecial(reply: @escaping (String?) -> Void) {
        run({ try self.core.charge.cancel() }, reply: reply)
    }
    func setPowerMode(_ mode: Int, reply: @escaping (String?) -> Void) {
        guard (0...2).contains(mode) else { reply("invalid power mode"); return }
        core.queue.async {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/bin/pmset")
            let supportsPowerMode = PowerMode.read().highPowerSupported
            p.arguments = supportsPowerMode ? ["-a", "powermode", "\(mode)"] : ["-a", "lowpowermode", mode == 1 ? "1" : "0"]
            p.standardOutput = FileHandle.nullDevice
            do { try p.run(); p.waitUntilExit() } catch { reply("\(error)"); return }
            reply(p.terminationStatus == 0 ? nil : "pmset exited with \(p.terminationStatus)")
        }
    }
    func version(reply: @escaping (String) -> Void) { reply(ClearPowerVersion.string) }
}

final class ListenerDelegate: NSObject, NSXPCListenerDelegate {
    let service: HelperService
    init(service: HelperService) { self.service = service }
    func listener(_ listener: NSXPCListener, shouldAcceptNewConnection conn: NSXPCConnection) -> Bool {
        // With a Developer ID build, scripts/build-app.sh writes the app's designated
        // requirement next to the helper; enforce it when present.
        if let req = try? String(contentsOfFile: helperInstallPath + ".requirement", encoding: .utf8),
           !req.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            if #available(macOS 13, *) { conn.setCodeSigningRequirement(req.trimmingCharacters(in: .whitespacesAndNewlines)) }
        }
        conn.exportedInterface = NSXPCInterface(with: HelperProtocol.self)
        conn.exportedObject = service
        conn.resume()
        return true
    }
}

// ---- sleep / wake ----
var powerNotifier: io_object_t = 0
var powerPort: IONotificationPortRef?
var helperCore: HelperCore!

// iokit_common_msg(...) macros are not imported into Swift.
let kMsgSystemWillSleep: UInt32 = 0xe0000280
let kMsgCanSystemSleep: UInt32 = 0xe0000270
let kMsgSystemHasPoweredOn: UInt32 = 0xe0000300

func installPowerNotifications() {
    let callback: IOServiceInterestCallback = { _, _, messageType, argument in
        switch messageType {
        case kMsgSystemWillSleep:
            helperCore.willSleep()
            IOAllowPowerChange(rootPort, Int(bitPattern: argument))
        case kMsgCanSystemSleep:
            IOAllowPowerChange(rootPort, Int(bitPattern: argument))
        case kMsgSystemHasPoweredOn:
            helperCore.didWake()
        default: break
        }
    }
    rootPort = IORegisterForSystemPower(nil, &powerPort, callback, &powerNotifier)
    if rootPort != 0, let port = powerPort {
        CFRunLoopAddSource(CFRunLoopGetMain(), IONotificationPortGetRunLoopSource(port).takeUnretainedValue(), .commonModes)
    }
}
var rootPort: io_connect_t = 0

// ---- main ----
if CommandLine.arguments.contains("--version") {
    print(ClearPowerVersion.string)
    exit(0)
}
guard getuid() == 0 else {
    log("must run as root (launchd daemon)")
    exit(1)
}
helperCore = HelperCore()
let service = HelperService(core: helperCore)
let delegate = ListenerDelegate(service: service)
let listener = NSXPCListener(machServiceName: helperMachServiceName)
listener.delegate = delegate
listener.resume()
installPowerNotifications()

let sigSrc = [SIGTERM, SIGINT].map { sig -> DispatchSourceTimer? in
    signal(sig, SIG_IGN)
    let src = DispatchSource.makeSignalSource(signal: sig, queue: .main)
    src.setEventHandler { helperCore.shutdown(); exit(0) }
    src.resume()
    return nil
}
_ = sigSrc
RunLoop.main.run()

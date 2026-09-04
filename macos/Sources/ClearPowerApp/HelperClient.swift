// XPC client for the privileged helper. Plays the role of daemonProxy.js on GNOME.
import Foundation
import ClearPowerIPC
import ClearPowerCore

final class HelperClient {
    private var connection: NSXPCConnection?
    private(set) var online = false
    private(set) var state: [String: Any] = [:]
    var onChange: (() -> Void)?

    var mode: String { state.s("charge_mode", "limit") }
    var limit: Int { state.i("charge_limit", 100) }
    var target: Int { state.i("charge_target", 0) }
    var controlSupported: Bool { state.b("control_supported", false) }
    var dischargeSupported: Bool { state.b("discharge_supported", false) }
    var helperVersion: String { state.s("version", "") }

    /// Keys merged into every engine snapshot (same names as the Linux daemon).
    var snapshotOverlay: [String: Any] {
        var o: [String: Any] = [:]
        for k in ["charge_mode", "charge_limit", "charge_target", "charge_behaviour",
                  "charge_start_threshold", "charge_end_threshold", "charging_inhibited", "adapter_disabled"] {
            if let v = state[k] { o[k] = v }
        }
        return o
    }

    private func proxy(_ onError: @escaping (String) -> Void) -> HelperProtocol? {
        if connection == nil {
            let c = NSXPCConnection(machServiceName: helperMachServiceName, options: .privileged)
            c.remoteObjectInterface = NSXPCInterface(with: HelperProtocol.self)
            c.invalidationHandler = { [weak self] in
                DispatchQueue.main.async { self?.connection = nil; self?.setOnline(false) }
            }
            c.interruptionHandler = { [weak self] in
                DispatchQueue.main.async { self?.setOnline(false) }
            }
            c.resume()
            connection = c
        }
        return connection?.remoteObjectProxyWithErrorHandler { [weak self] err in
            DispatchQueue.main.async { self?.setOnline(false) }
            onError(Self.describe(err))
        } as? HelperProtocol
    }

    private func setOnline(_ v: Bool) {
        if online != v { online = v; onChange?() }
    }

    static func describe(_ e: Error) -> String {
        let ns = e as NSError
        if ns.domain == NSCocoaErrorDomain && ns.code == 4099 { return "helper not installed" }
        return ns.localizedDescription
    }

    /// Poll the helper's state; called every engine tick.
    func refresh() {
        proxy { _ in }?.getState { [weak self] data in
            DispatchQueue.main.async {
                guard let self = self else { return }
                if let d = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                    self.state = d
                }
                self.setOnline(true)
                self.onChange?()
            }
        }
    }

    private func call(_ f: (HelperProtocol, @escaping (String?) -> Void) -> Void, completion: @escaping (String?) -> Void) {
        guard let p = proxy({ completion($0) }) else { completion("helper not installed"); return }
        f(p) { err in
            DispatchQueue.main.async {
                completion(err)
                self.refresh()
            }
        }
    }

    func setChargeLimit(_ pct: Int, completion: @escaping (String?) -> Void) {
        call({ $0.setChargeLimit(pct, reply: $1) }, completion: completion)
    }
    func startTopUp(completion: @escaping (String?) -> Void) {
        call({ $0.startTopUp(reply: $1) }, completion: completion)
    }
    func startDischarge(_ target: Int, completion: @escaping (String?) -> Void) {
        call({ $0.startDischarge(target, reply: $1) }, completion: completion)
    }
    func cancelSpecial(completion: @escaping (String?) -> Void) {
        call({ $0.cancelSpecial(reply: $1) }, completion: completion)
    }
    func setPowerMode(_ mode: Int, completion: @escaping (String?) -> Void) {
        call({ $0.setPowerMode(mode, reply: $1) }, completion: completion)
    }
    func version(completion: @escaping (String?) -> Void) {
        guard let p = proxy({ _ in completion(nil) }) else { completion(nil); return }
        p.version { v in DispatchQueue.main.async { completion(v) } }
    }
}

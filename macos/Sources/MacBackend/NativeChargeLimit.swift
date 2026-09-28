import Foundation
import CSupport
import ClearPowerCore

protocol NativeChargeControl: AnyObject {
    var limits: [Int] { get }
    func state() throws -> (limit: Int, enabled: Bool)
    func set(_ limit: Int) throws
    func disable() throws
}

final class NativeChargeLimit: NativeChargeControl {
    private let client: UnsafeMutableRawPointer
    let limits: [Int]

    init?() {
        guard let client = cp_charge_open() else { return nil }
        var values = [Int32](repeating: 0, count: 101)
        let count = Int(cp_charge_limits(client, &values, Int32(values.count)))
        guard count > 0 else { cp_charge_close(client); return nil }
        let valid = values.prefix(count).map(Int.init).filter { (50...100).contains($0) }
        guard valid.contains(100) else { cp_charge_close(client); return nil }
        self.client = client
        limits = Array(Set(valid)).sorted()
    }
    deinit { cp_charge_close(client) }

    private func call(_ body: (UnsafeMutablePointer<CChar>, Int32) -> Int32) throws {
        var error = [CChar](repeating: 0, count: 1024)
        let result = body(&error, Int32(error.count))
        guard result == 0 else { throw ChargeError(errno: result, String(cString: error)) }
    }
    func state() throws -> (limit: Int, enabled: Bool) {
        var limit: Int32 = 0, enabled: Int32 = 0
        try call { cp_charge_get(client, &limit, &enabled, $0, $1) }
        return (Int(limit), enabled != 0)
    }
    func set(_ limit: Int) throws {
        guard limits.contains(limit) else { throw ChargeError(errno: 22, "Supported charge limits: \(limits)") }
        try call { cp_charge_set(client, Int32(limit), $0, $1) }
        let applied = try state()
        guard applied.limit == limit && (limit == 100 || applied.enabled) else { throw ChargeError(errno: 5, "macOS did not apply the charge limit") }
    }
    func disable() throws { try call { cp_charge_disable(client, $0, $1) } }
}

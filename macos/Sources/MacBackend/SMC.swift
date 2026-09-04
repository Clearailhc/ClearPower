// Thin Swift wrapper over the AppleSMC user client (CSupport). Reads work from a user
// session; writes need root. Apple Silicon SMC integers are little-endian.
import Foundation
import CSupport

public struct SMCValue {
    public let type: String
    public let bytes: [UInt8]

    public var float: Double? {
        switch type {
        case "flt ":
            guard bytes.count >= 4 else { return nil }
            return Double(Float(bitPattern: UInt32(bytes[0]) | UInt32(bytes[1]) << 8 | UInt32(bytes[2]) << 16 | UInt32(bytes[3]) << 24))
        case "ui8 ": return bytes.first.map(Double.init)
        case "si8 ": return bytes.first.map { Double(Int8(bitPattern: $0)) }
        case "ui16": return bytes.count >= 2 ? Double(UInt16(bytes[0]) | UInt16(bytes[1]) << 8) : nil
        case "si16": return bytes.count >= 2 ? Double(Int16(bitPattern: UInt16(bytes[0]) | UInt16(bytes[1]) << 8)) : nil
        case "ui32": return bytes.count >= 4 ? Double(UInt32(bytes[0]) | UInt32(bytes[1]) << 8 | UInt32(bytes[2]) << 16 | UInt32(bytes[3]) << 24) : nil
        case "sp78": return bytes.count >= 2 ? Double(Int16(bitPattern: UInt16(bytes[1]) | UInt16(bytes[0]) << 8)) / 256 : nil
        case "fpe2": return bytes.count >= 2 ? Double(UInt16(bytes[1]) | UInt16(bytes[0]) << 8) / 4 : nil
        default: return nil
        }
    }
}

public enum SMC {
    public static var available: Bool { cp_smc_open() == 0 }

    public static func read(_ key: String) -> SMCValue? {
        var v = cp_smc_value()
        guard cp_smc_read(key, &v) == 0 else { return nil }
        let type = withUnsafeBytes(of: v.type) { String(bytes: $0.prefix(4), encoding: .ascii) ?? "" }
        let bytes = withUnsafeBytes(of: v.bytes) { Array($0.prefix(Int(v.size))) }
        return SMCValue(type: type, bytes: bytes)
    }

    public static func readFloat(_ key: String) -> Double? { read(key)?.float }

    public static func exists(_ key: String) -> Bool { read(key) != nil }

    /// Write raw bytes; size must match the key's declared size. Root only.
    public static func write(_ key: String, _ bytes: [UInt8]) -> Int32 {
        bytes.withUnsafeBufferPointer { cp_smc_write(key, $0.baseAddress, UInt32(bytes.count)) }
    }

    public static func allKeys() -> [String] {
        let n = cp_smc_key_count()
        guard n > 0 else { return [] }
        var out: [String] = []
        out.reserveCapacity(Int(n))
        var buf = [CChar](repeating: 0, count: 5)
        for i in 0..<UInt32(n) {
            if cp_smc_key_at(i, &buf) == 0 { out.append(String(cString: buf)) }
        }
        return out
    }
}

// Built-in display brightness via DisplayServices (private). Raw range 0...1000 so the
// calibration table has integer keys like the Linux backlight interface.
import Foundation
import CSupport
import ClearPowerCore

public final class Brightness: BrightnessControl {
    public let max = 1000
    public init() {}

    public var available: Bool {
        var f: Float = 0
        return cp_brightness_get(&f) == 0
    }

    public func readRaw() -> Int? {
        var f: Float = 0
        guard cp_brightness_get(&f) == 0 else { return nil }
        return Int((Double(f) * Double(max)).rounded())
    }

    public func setRaw(_ value: Int) throws {
        if cp_brightness_set(Float(Double(value) / Double(max))) != 0 {
            throw ChargeError(errno: 5, "DisplayServicesSetBrightness failed")
        }
    }

    public var displayOn: Bool { cp_display_asleep() != 1 }

    /// Snapshot keys brightness_pct, brightness_raw, display_on.
    public func read() -> [String: Any] {
        if let raw = readRaw() {
            return ["brightness_pct": Double(raw) / 10.0, "brightness_raw": raw, "display_on": displayOn]
        }
        return ["brightness_pct": -1.0, "brightness_raw": -1, "display_on": true]
    }
}

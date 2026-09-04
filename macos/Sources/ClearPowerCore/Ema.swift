// Exponential moving average with a wall-clock time constant.
// Port of daemon/clearpowerd/smoothing.py.
import Foundation

public struct Ema {
    public let tau: Double
    public private(set) var value: Double?
    private var t: Double?

    public init(tau: Double) {
        self.tau = max(tau, 0)
    }

    @discardableResult
    public mutating func update(_ x: Double, at t: Double) -> Double {
        if let v = value, tau > 0, let t0 = self.t {
            let a = 1.0 - exp(-max(t - t0, 0) / tau)
            value = v + a * (x - v)
        } else {
            value = x
        }
        self.t = t
        return value!
    }

    public mutating func reset() {
        value = nil
    }
}

// The sampling loop: sampler + charge-state overlay + history + runtime estimate + display
// calibration + top processes. Mirrors tick() in daemon/clearpowerd/__main__.py, but runs
// inside the app (no root needed for any of it). Charge control lives in the helper.
import Foundation
import os
import ClearPowerCore

public let engineLogger = Logger(subsystem: "org.clearpower", category: "engine")

public struct EngineConfig {
    public var sampleIntervalMs = 1000     // while a client is watching (popover open)
    public var idleIntervalMs = 2000       // nobody asked for details in the last `hotSeconds`
    public var hotSeconds = 6.0
    public var procsIntervalS = 3.0
    public var smoothingS = 5.0
    public var historySeconds = 24 * 3600
    public var historyStepS = 10
    public init() {}
}

public final class Engine {
    public let cfg: EngineConfig
    public let sampler: Sampler
    public let displayCal: DisplayCalibration
    public private(set) var runtime = RuntimeEstimator()
    public private(set) var history: History
    private var procs = ProcessBudget()
    private let procSource = ProcessSource()
    public private(set) var topProcesses: [(name: String, w: Double, cpuPct: Double)] = []
    public private(set) var snapshot: [String: Any] = [:]
    private var lastClientActivity = -1e9
    private var timer: DispatchSourceTimer?
    private var currentInterval = 0
    public var onSample: (([String: Any]) -> Void)?
    /// Charge state from the helper, merged into every snapshot (charge_mode/limit/target,
    /// charge_behaviour, thresholds). Set by the helper client.
    public var chargeState: [String: Any] = ["charge_mode": "limit", "charge_limit": 100, "charge_target": 0,
                                             "charge_behaviour": "auto", "charge_start_threshold": 95, "charge_end_threshold": 100]
    public var log: (String) -> Void = { s in engineLogger.notice("\(s, privacy: .public)") }

    public static var stateDirectory: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        return base.appendingPathComponent("ClearPower", isDirectory: true)
    }

    public init(config: EngineConfig = EngineConfig()) {
        cfg = config
        displayCal = DisplayCalibration(brightness: Brightness(), storage: Self.stateDirectory.appendingPathComponent("display_cal.json"))
        sampler = Sampler(smoothingS: config.smoothingS, displayCal: displayCal)
        history = History(seconds: config.historySeconds, stepS: config.historyStepS)
        procs = ProcessBudget(intervalS: config.procsIntervalS)
        displayCal.log = { [weak self] s in self?.log(s) }
        sampler.log = { [weak self] s in self?.log(s) }
    }

    /// A client asked for details (popover open): sample at full rate for `hotSeconds`.
    public func touch() { lastClientActivity = monotonicNow() }
    public var hot: Bool { monotonicNow() - lastClientActivity < cfg.hotSeconds }

    private var ticking = false

    @discardableResult
    public func tick() -> [String: Any] {
        if ticking { return snapshot }  // never re-enter (run loop pumping inside a tick)
        ticking = true
        defer { ticking = false }
        let hot = self.hot
        var snap = sampler.sample(hot: hot)
        snap.merge(chargeState) { $1 }
        let now = monotonicNow()
        runtime.add(t: now, energyWh: snap.d("bat_energy_wh", 0), status: snap.s("bat_status"))
        let limit = snap.i("charge_limit", 100)
        let targetWh = snap.d("bat_full_wh", 0) * Double(limit) / 100
        snap.merge(runtime.estimate(energyNowWh: snap.d("bat_energy_wh", 0), targetWh: targetWh, fallbackW: abs(snap.d("bat_w", 0)))) { $1 }
        displayCal.tick(restRawW: sampler.raw.rest, now: now)
        snap.merge(displayCal.snapshotKeys) { $1 }
        history.add(snap)
        if hot {
            let pkg = snap.d("package_w")
            topProcesses = procs.sample(now: now, packageW: pkg, usage: { procSource.usage(now: now) })
        }
        snapshot = snap
        onSample?(snap)
        return snap
    }

    /// Adaptive rate: 1 Hz while someone is looking or a special mode / calibration is
    /// active, slower otherwise.
    private func desiredInterval() -> Int {
        let busy = hot || snapshot.s("charge_mode", "limit") != "limit" || displayCal.state == "running"
        return busy ? cfg.sampleIntervalMs : cfg.idleIntervalMs
    }

    public func start(queue: DispatchQueue = .main) {
        stop()
        timer = nil
        schedule(ms: desiredInterval(), queue: queue)
    }

    private func schedule(ms: Int, queue: DispatchQueue) {
        // First arm fires immediately; a rate change waits a full interval so two samples
        // are never taken back to back (energy counters need a real delta).
        let first = timer == nil
        currentInterval = ms
        let t = DispatchSource.makeTimerSource(queue: queue)
        t.schedule(deadline: .now() + .milliseconds(first ? 0 : ms), repeating: .milliseconds(ms), leeway: .milliseconds(ms / 10))
        t.setEventHandler { [weak self] in
            guard let self = self else { return }
            self.tick()
            let want = self.desiredInterval()
            if want != self.currentInterval { self.timer?.cancel(); self.schedule(ms: want, queue: queue) }
        }
        timer = t
        t.resume()
    }

    public func stop() {
        timer?.cancel()
        timer = nil
    }

    public func getHistory(_ field: String, seconds: Double) -> [(Double, Double)] {
        history.get(field, seconds: seconds)
    }

    // ---- display calibration / content ----
    public func calibrateDisplay() {
        displayCal.start(now: monotonicNow(), energyOK: sampler.energyAvailable)
    }
    public func cancelCalibration() { displayCal.cancel() }
    public func setDisplayContent(_ apl: Double) {
        touch()
        displayCal.setContent(apl, now: monotonicNow())
    }
}

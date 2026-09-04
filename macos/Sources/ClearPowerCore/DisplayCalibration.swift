// Display power calibration for panels without a power sensor.
// Port of daemon/clearpowerd/display_cal.py.
//
// The only knobs we control are brightness and (indirectly) picture content; the only
// truth we have is platform power minus the measured SoC and memory, i.e. "everything
// else". Sweeping brightness while the machine is idle yields the panel's emission per
// level. For OLED the emission also scales with the average picture luminance, so the
// table is normalised to a white screen and re-scaled at runtime with the live content
// level supplied by the UI.
import Foundation

public protocol BrightnessControl: AnyObject {
    var available: Bool { get }
    /// Raw brightness range is 0...max (macOS: 0...1000).
    var max: Int { get }
    func readRaw() -> Int?
    func setRaw(_ value: Int) throws
}

public final class DisplayCalibration {
    public static let levels: [Double] = [0.0, 0.01, 0.1, 0.25, 0.5, 0.75, 1.0]
    public static let settleS = 1.5
    public static let samples = 5
    public static let sampleGapS = 1.0

    public private(set) var state = "idle"  // idle | running | done | failed
    public private(set) var progress = 0.0
    public private(set) var message = ""
    public private(set) var table: [(raw: Int, w: Double)] = []
    public private(set) var aplCal = -1.0
    public private(set) var aplMeasured = -1.0
    public private(set) var rest0 = -1.0
    public private(set) var calibratedAt = 0.0
    public private(set) var apl = -1.0
    private var aplTs = 0.0
    private let bl: BrightnessControl
    private let path: URL?
    public var log: (String) -> Void = { _ in }

    private struct Run {
        var orig: Int
        var idx = 0
        var phaseT: Double
        var levelRaw = 0
        var lastSampleT = 0.0
        var samples: [Double] = []
        var results: [(Int, Double)] = []
        var apls: [Double] = []
    }
    private var run: Run? = nil

    public init(brightness: BrightnessControl, storage: URL?) {
        bl = brightness
        path = storage
        load()
    }

    public var calibrated: Bool { table.count >= 2 }

    // ---- persistence ----
    private func load() {
        guard let path = path, let data = try? Data(contentsOf: path),
              let d = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let t = d["table"] as? [[Any]] else { return }
        table = t.compactMap { p in
            guard p.count == 2, let r = Snapshot.double(p[0]), let w = Snapshot.double(p[1]) else { return nil }
            return (Int(r), w)
        }
        aplCal = 1.0  // table is normalised to a white screen (see finish)
        rest0 = Snapshot.double(d["rest0"]) ?? -1
        calibratedAt = Snapshot.double(d["calibrated_at"]) ?? 0
        state = table.isEmpty ? "idle" : "done"
    }

    private func save() {
        guard let path = path else { return }
        let obj: [String: Any] = [
            "table": table.map { [$0.raw, $0.w] }, "apl_cal": aplCal, "apl_measured": aplMeasured,
            "rest0": rest0, "calibrated_at": calibratedAt, "max_brightness": bl.max,
        ]
        do {
            try FileManager.default.createDirectory(at: path.deletingLastPathComponent(), withIntermediateDirectories: true)
            let data = try JSONSerialization.data(withJSONObject: obj)
            try data.write(to: path, options: .atomic)
        } catch {
            log("cannot save calibration: \(error)")
        }
    }

    /// Test/seed hook: install a table directly.
    public func setTable(_ t: [(Int, Double)], rest0: Double = -1) {
        table = t.map { (raw: $0.0, w: $0.1) }
        aplCal = 1.0
        self.rest0 = rest0
        state = calibrated ? "done" : "idle"
    }

    // ---- content level from the UI ----
    public func setContent(_ value: Double, now: Double) {
        apl = Swift.max(0, Swift.min(1, value))
        aplTs = now
        if state == "running", run != nil { run!.apls.append(apl) }
    }

    private func freshApl(_ now: Double) -> Double {
        (apl >= 0 && now - aplTs < 60) ? apl : -1
    }

    // ---- runtime estimate ----
    public func emissionW(rawBrightness: Int?, now: Double) -> Double {
        guard calibrated, let rb = rawBrightness else { return -1 }
        let pts = table
        var e: Double
        if rb <= pts[0].raw {
            e = pts[0].w
        } else if rb >= pts[pts.count - 1].raw {
            e = pts[pts.count - 1].w
        } else {
            e = pts[pts.count - 1].w
            for i in 0..<(pts.count - 1) {
                let (b0, e0) = pts[i], (b1, e1) = pts[i + 1]
                if b0 <= rb && rb <= b1 {
                    e = e0 + (e1 - e0) * Double(rb - b0) / Double(Swift.max(b1 - b0, 1))
                    break
                }
            }
        }
        let a = freshApl(now)
        if a >= 0 && aplCal > 0.02 {
            e *= Swift.max(0.02, Swift.min(1.2, a / aplCal))
        }
        return e
    }

    // ---- state machine ----
    public func start(now: Double, energyOK: Bool) {
        if state == "running" { return }
        if !bl.available { state = "failed"; message = "no backlight device"; return }
        if !energyOK { state = "failed"; message = "SoC energy counters not readable; cannot isolate the display"; return }
        let orig = bl.readRaw() ?? bl.max
        run = Run(orig: orig, phaseT: now)
        state = "running"; progress = 0; message = ""
        applyLevel(now)
        log("display calibration started (orig brightness \(orig))")
    }

    private func applyLevel(_ now: Double) {
        guard var r = run else { return }
        let raw = Int((Self.levels[r.idx] * Double(bl.max)).rounded())
        do {
            try bl.setRaw(raw)
        } catch {
            finish(failed: "cannot set brightness: \(error)")
            return
        }
        r.levelRaw = raw; r.phaseT = now; r.samples = []; r.lastSampleT = 0
        run = r
    }

    public func tick(restRawW: Double, now: Double) {
        guard state == "running", var r = run else { return }
        if now - r.phaseT < Self.settleS { return }
        if restRawW < 0 { finish(failed: "platform power unavailable"); return }
        if now - r.lastSampleT >= Self.sampleGapS {
            r.samples.append(restRawW)
            r.lastSampleT = now
        }
        progress = (Double(r.idx) + Double(r.samples.count) / Double(Self.samples)) / Double(Self.levels.count)
        if r.samples.count >= Self.samples {
            r.results.append((r.levelRaw, Self.median(r.samples)))
            r.idx += 1
            run = r
            if r.idx >= Self.levels.count { finish() } else { applyLevel(now) }
        } else {
            run = r
        }
    }

    public func cancel() {
        if state == "running" { finish(failed: "cancelled") }
    }

    static func median(_ xs: [Double]) -> Double {
        let s = xs.sorted()
        let n = s.count
        if n == 0 { return -1 }
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2
    }

    private func finish(failed: String? = nil) {
        if let r = run {
            do { try bl.setRaw(r.orig) } catch { log("could not restore brightness: \(error)") }
        }
        if let failed = failed {
            state = calibrated ? "done" : "failed"
            message = failed
            log("display calibration aborted: \(failed)")
        } else if let r = run {
            let res = r.results.sorted { $0.0 < $1.0 }
            let r0 = res[0].1
            var t: [(raw: Int, w: Double)] = []
            var runningMax = 0.0
            for (raw, rest) in res {
                runningMax = Swift.max(runningMax, rest - r0)  // emission can only grow with brightness
                t.append((raw, (runningMax * 1000).rounded() / 1000))
            }
            table = t
            rest0 = (r0 * 1000).rounded() / 1000
            // The UI shows a white screen during the sweep, so by construction the table is
            // the emission for content level 1.0. The measured level is only a sanity check.
            let apls = r.apls
            let measured = apls.count >= 2 ? Self.median(Array(apls[(apls.count / 3)...])) : -1
            if measured >= 0 && measured < 0.8 {
                log(String(format: "calibration screen content level was %.2f (expected ~1.0)", measured))
            }
            aplCal = 1.0
            aplMeasured = measured
            calibratedAt = Date().timeIntervalSince1970
            state = "done"; progress = 1; message = ""
            save()
            log("display calibration done: \(t) (rest0 \(rest0))")
        }
        run = nil
    }

    public var snapshotKeys: [String: Any] {
        [
            "display_calibrated": calibrated,
            "calib_state": state,
            "calib_progress": progress,
            "calib_message": message,
            "calibrated_at": calibratedAt,
            "content_apl": apl,
        ]
    }
}

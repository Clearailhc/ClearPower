// Golden tests: the Python daemon (daemon/clearpowerd) is the reference. Fixtures are
// produced by macos/scripts/gen-fixtures.py and compared value by value.
import Testing
import Foundation
@testable import ClearPowerCore

private func fixture(_ name: String) -> Any {
    let url = Bundle.module.url(forResource: name, withExtension: "json", subdirectory: "Fixtures")!
    return try! JSONSerialization.jsonObject(with: Data(contentsOf: url))
}

private func near(_ a: Double, _ b: Double, _ tol: Double = 1e-9, _ msg: String = "", sourceLocation: SourceLocation = #_sourceLocation) {
    let ok = abs(a - b) <= max(tol, abs(b) * 1e-9)
    #expect(ok, "\(msg): got \(a), expected \(b)", sourceLocation: sourceLocation)
}

struct EmaGoldenTests {
    @Test func ema() {
        for c in fixture("ema") as! [[String: Any]] {
            var e = Ema(tau: c["tau"] as! Double)
            let steps = c["steps"] as! [[String: Any]]
            for (i, s) in steps.enumerated() {
                if i == steps.count - 1 { e.reset() }
                let v = e.update(s["x"] as! Double, at: s["t"] as! Double)
                near(v, s["v"] as! Double, 1e-9, "tau \(c["tau"]!) step \(i)")
            }
        }
    }
}

struct RuntimeGoldenTests {
    @Test func runtime() {
        var rt = RuntimeEstimator()
        // Fixture only records every 7th step (plus the first 7); replay the same plan.
        var t = 0.0, e = 60.0
        let plan: [(String, Double, Int)] = [("Discharging", 12, 180), ("Charging", -30, 120), ("Not charging", 0, 12), ("Discharging", 8, 60)]
        let recorded = fixture("runtime") as! [[String: Any]]
        var ri = 0, i = 0
        for (status, w, n) in plan {
            for _ in 0..<n {
                t += 10; e -= w * 10 / 3600
                rt.add(t: t, energyWh: e, status: status)
                i += 1
                if i % 7 == 0 || i < 8 {
                    let r = recorded[ri]; ri += 1
                    near(t, r["t"] as! Double)
                    let out = rt.estimate(energyNowWh: e, targetWh: r["target_wh"] as! Double, fallbackW: r["fallback_w"] as! Double)
                    let exp = r["out"] as! [String: Any]
                    for (k, v) in exp {
                        near(Snapshot.double(out[k])!, Snapshot.double(v)!, 1e-6, "step \(i) key \(k)")
                    }
                }
            }
        }
        #expect(ri == recorded.count)
    }
}

struct PowerModelGoldenTests {
    @Test func breakdown() {
        var m = PowerModel(smoothingS: 5)
        for (i, c) in (fixture("power_model") as! [[String: Any]]).enumerated() {
            let inp = c["in"] as! [String: Any]
            let raw = RawPower(batW: inp["bat_w"] as! Double, psys: inp["psys"] as! Double, package: inp["package"] as! Double,
                               core: inp["core"] as! Double, uncore: inp["uncore"] as! Double, dram: inp["dram"] as! Double)
            let out = m.update(raw: raw, onAC: inp["on_ac"] as! Bool, now: c["t"] as! Double,
                               displayEmission: inp["emission"] as! Double, displayOn: inp["display_on"] as! Bool)
            near(m.raw.rest, c["raw_rest"] as! Double, 1e-9, "step \(i) rest")
            for (k, v) in c["out"] as! [String: Any] {
                if let s = v as? String { #expect(out[k] as? String == s, Comment(rawValue: "step \(i) \(k)")) }
                else { near(Snapshot.double(out[k])!, Snapshot.double(v)!, 1e-9, "step \(i) \(k)") }
            }
        }
    }
}

final class FakeChargeHW: ChargeHardware {
    var thresholdsSupported = true
    var behaviours = ["auto", "inhibit-charge", "force-discharge"]
    var writes: [[Any]] = []
    var saved: Int? = nil
    func writeThresholds(start: Int, end: Int) throws {
        // Linux writes them in an order dictated by sysfs; the fixture is normalised below.
        writes.append(["start", start]); writes.append(["end", end])
    }
    func writeBehaviour(_ behaviour: String) throws { writes.append(["beh", behaviour]) }
    func loadLimit() -> Int? { nil }
    func saveLimit(_ limit: Int) { saved = limit }
}

struct ChargeGoldenTests {
    @Test func stateMachine() throws {
        let hw = FakeChargeHW()
        let c = ChargeStateMachine(hardware: hw, dischargeFloorPct: 20)
        for step in fixture("charge") as! [[String: Any]] {
            let op = step["op"] as! String
            let args = step["args"] as! [String: Any]
            switch op {
            case "startup": c.applyStartup()
            case "set_limit": try c.setLimit(args["pct"] as! Int)
            case "start_topup": try c.startTopUp()
            case "start_discharge": try c.startDischarge(target: args["target"] as! Int)
            case "cancel": try c.cancel()
            case "shutdown": c.shutdown()
            case "tick": c.tick(batPct: args["pct"] as! Int, batStatus: args["status"] as! String)
            default: Issue.record("unknown op \(op)")
            }
            let st = step["state"] as! [String: Any]
            #expect(c.mode.rawValue == st["charge_mode"] as! String, Comment(rawValue: String(describing: op)))
            #expect(c.limit == st["charge_limit"] as! Int, Comment(rawValue: String(describing: op)))
            #expect(c.target == st["charge_target"] as! Int, Comment(rawValue: String(describing: op)))
            // Compare the set of writes ignoring the sysfs-specific threshold ordering.
            let expected = (step["writes"] as! [[Any]]).map { "\($0[0]):\($0[1])" }.sorted()
            let got = hw.writes.map { "\($0[0]):\($0[1])" }.sorted()
            #expect(got == expected, Comment(rawValue: String(describing: op)))
            hw.writes = []
        }
    }
}

final class FakeBrightness: BrightnessControl {
    var available = true
    var max = 1000
    var value = 700
    var sets: [Int] = []
    func readRaw() -> Int? { value }
    func setRaw(_ v: Int) throws { value = v; sets.append(v) }
}

struct DisplayCalGoldenTests {
    @Test func sweepAndInterpolation() {
        let fx = fixture("display_cal") as! [String: Any]
        let bl = FakeBrightness()
        let d = DisplayCalibration(brightness: bl, storage: nil)
        var now = 100.0
        d.start(now: now, energyOK: true)
        #expect(d.state == "running")
        var guardCount = 0
        while d.state == "running" && guardCount < 10000 {
            now += 0.5
            let lvl = Double(bl.value) / 1000.0
            let noise = [0.3, -0.2, 0.1, 0.0, -0.1][Int(now * 2) % 5]
            d.tick(restRawW: 3.0 + 6.0 * lvl + noise, now: now)
            guardCount += 1
        }
        #expect(d.state == fx["final_state"] as! String)
        #expect(bl.sets == fx["sets"] as! [Int])
        let table = fx["table"] as! [[Double]]
        #expect(d.table.count == table.count)
        for (p, q) in zip(d.table, table) {
            #expect(Double(p.raw) == q[0]); near(p.w, q[1], 1e-9)
        }
        near(d.rest0, fx["rest0"] as! Double, 1e-9)
        for c in fx["interp"] as! [[String: Any]] {
            let raw = c["raw"] as! Int
            let apl = c["apl"] as! Double
            if let w = c["w"] as? Double {
                if apl >= 0 { d.setContent(apl, now: now) } else { d.setContent(-1, now: now - 100) }
                near(d.emissionW(rawBrightness: raw, now: now), w, 1e-9, "raw \(raw) apl \(apl)")
            } else if let w = c["w_stale"] as? Double {
                d.setContent(apl, now: now)
                near(d.emissionW(rawBrightness: raw, now: now + 61), w, 1e-9, "stale")
            }
        }
    }
}

struct HistoryGoldenTests {
    @Test func downsampling() {
        let fx = fixture("history") as! [String: Any]
        var h = History(seconds: 120, stepS: 10)
        for s in fx["snaps"] as! [[String: Any]] { h.add(s) }
        let get = fx["get"] as! [String: Any]
        for f in ["sys_w", "soc_w", "bat_pct"] {
            let exp = get[f] as! [[Double]]
            let got = h.get(f, seconds: 60)
            #expect(got.count == exp.count, Comment(rawValue: String(describing: f)))
            for (a, b) in zip(got, exp) { near(a.0, b[0]); near(a.1, b[1], 1e-9, f) }
        }
        let all = get["all_sys"] as! [[Double]]
        #expect(h.get("sys_w", seconds: 1e9).count == all.count)
    }
}

struct I18nTests {
    @Test func resolveAndFormat() {
        #expect(I18n.resolveLanguage("system", systemLanguages: ["zh-Hans-CN", "en"]) == "zh_CN")
        #expect(I18n.resolveLanguage("system", systemLanguages: ["en-US"]) == "en")
        #expect(I18n.resolveLanguage("zh-cn", systemLanguages: ["en"]) == "zh_CN")
        I18n.setLanguage("en", systemLanguages: [])
        #expect(I18n.t("limit", ["n": 80]) == "Limit 80%")
        #expect(I18n.fmtDuration(minutes: 125) == "2 h 5 m")
        #expect(I18n.fmtDuration(minutes: 12.4) == "12 min")
        I18n.setLanguage("zh-cn", systemLanguages: [])
        #expect(I18n.fmtDuration(minutes: 125) == "2 小时 5 分")
        #expect(I18n.t("missingKey") == "missingKey")
        #expect(Set(I18n.strings["en"]!.keys) == Set(I18n.strings["zh_CN"]!.keys), Comment(rawValue: String(describing: "zh/en key sets differ")))
    }
}

struct ProcessBudgetTests {
    @Test func budgetDistribution() {
        var pb = ProcessBudget(intervalS: 3, floorWindowS: 600)
        _ = pb.sample(now: 0, packageW: 4, usage: { [] })
        let top = pb.sample(now: 10, packageW: 10, usage: { [("a", 50), ("b", 25), ("a", 25), ("c", 0), ("d", 5)] })
        // floor is min over window = 4, budget 6 W; a=75 of 105
        #expect(top.count == 3)
        #expect(top[0].name == "a"); near(top[0].w, 6 * 75.0 / 105.0, 1e-9)
        #expect(top[1].name == "b"); #expect(top[2].name == "d")
        // Called again within interval -> cached
        #expect(pb.sample(now: 11, packageW: 20, usage: { [("z", 1)] }).first?.name == "a")
    }
}

import Foundation
import AppKit
import ClearPowerCore
import MacBackend

// `--once`: print one snapshot as JSON plus a parts-vs-total check (mirrors
// `python3 -m clearpowerd --once`). Two samples are taken so energy counters have a delta.
if CommandLine.arguments.contains("--once") {
    let engine = Engine()
    engine.touch()
    _ = engine.tick()
    Thread.sleep(forTimeInterval: 1.0)
    let snap = engine.tick()
    print(Snapshot.json(snap))
    let parts = ["cpu_w", "gpu_w", "soc_w", "mem_w", "display_w", "other_w"].map { snap.d($0) }.filter { $0 >= 0 }
    let sum = parts.reduce(0, +)
    print(String(format: "# sum(parts)=%.3f  sys_w=%.3f  source=%@", sum, snap.d("sys_w"), snap.s("sys_source")))
    if CommandLine.arguments.contains("-v") {
        print("# IOReport channels (W):")
        for (n, w) in engine.sampler.energy.lastChannels.sorted(by: { $0.1 > $1.1 }) where w > 0.005 {
            print(String(format: "#   %-24@ %7.3f", n, w))
        }
        print("# top processes:", engine.topProcesses.map { "\($0.name) \(String(format: "%.2f", $0.w)) W" })
    }
    exit(0)
}
// `--loop N`: N ticks at 1 Hz with the popover considered open, one line each (debugging).
if let i = CommandLine.arguments.firstIndex(of: "--loop") {
    let n = Int(CommandLine.arguments[safe: i + 1] ?? "") ?? 20
    let engine = Engine()
    var count = 0
    engine.onSample = { snap in
        count += 1
        print(String(format: "%3d sys=%6.2f cpu=%6.2f gpu=%6.2f soc=%6.2f mem=%6.2f other=%6.2f src=%@ pkg_raw=%6.2f",
                     count, snap.d("sys_w"), snap.d("cpu_w"), snap.d("gpu_w"), snap.d("soc_w"), snap.d("mem_w"), snap.d("other_w"),
                     snap.s("sys_source"), engine.sampler.raw.package))
        if count >= n { exit(0) }
    }
    if CommandLine.arguments.contains("--engine-timer") {
        // Use the engine's own adaptive timer, touching it every second like the popover does.
        let touch = DispatchSource.makeTimerSource(queue: .main)
        touch.schedule(deadline: .now(), repeating: 1.0)
        touch.setEventHandler { engine.touch() }
        touch.resume()
        engine.start()
    } else {
        let t = DispatchSource.makeTimerSource(queue: .main)
        t.schedule(deadline: .now(), repeating: 1.0)
        t.setEventHandler { engine.touch(); engine.tick() }
        t.resume()
    }
    RunLoop.main.run()
}

extension Array { subscript(safe i: Int) -> Element? { indices.contains(i) ? self[i] : nil } }

// Development CLI for the helper: --helper state|limit N|topup|discharge N|cancel|powermode N|install|remove
if let i = CommandLine.arguments.firstIndex(of: "--helper") {
    let args = Array(CommandLine.arguments[(i + 1)...])
    let client = HelperClient()
    func finish(_ err: String?) {
        if let e = err { print("error: \(e)"); exit(1) }
        client.refresh()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
            print(Snapshot.json(client.state)); exit(0)
        }
    }
    switch args.first ?? "state" {
    case "state": finish(nil)
    case "limit": client.setChargeLimit(Int(args[1]) ?? 100, completion: finish)
    case "topup": client.startTopUp(completion: finish)
    case "discharge": client.startDischarge(args.count > 1 ? Int(args[1]) ?? 0 : 0, completion: finish)
    case "cancel": client.cancelSpecial(completion: finish)
    case "powermode": client.setPowerMode(Int(args[1]) ?? 0, completion: finish)
    case "install": print(HelperInstaller.install() ?? "installed"); exit(0)
    case "remove": print(HelperInstaller.remove() ?? "removed"); exit(0)
    default: print("unknown helper command"); exit(2)
    }
    DispatchQueue.main.asyncAfter(deadline: .now() + 5) { print("error: timeout (helper not responding)"); exit(1) }
    RunLoop.main.run()
}
ClearPowerApp.main()

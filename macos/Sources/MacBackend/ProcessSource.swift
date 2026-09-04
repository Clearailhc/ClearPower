// Per-process CPU time deltas via libproc, aggregated by application name.
// Replaces the psutil part of daemon/clearpowerd/sources/procs.py.
import Foundation
import Darwin

public final class ProcessSource {
    private var last: [pid_t: (cpuNs: UInt64, name: String)] = [:]
    private var lastT: Double = 0

    public init() {}

    /// Application-ish name: the enclosing `.app` bundle if any, else the executable name.
    private static func name(for pid: pid_t) -> String {
        var buf = [CChar](repeating: 0, count: Int(4 * MAXPATHLEN))
        let n = proc_pidpath(pid, &buf, UInt32(buf.count))
        if n > 0 {
            let path = String(cString: buf)
            let parts = path.split(separator: "/")
            if let app = parts.first(where: { $0.hasSuffix(".app") }) {
                return String(app.dropLast(4))
            }
            return String(parts.last ?? "?")
        }
        var nb = [CChar](repeating: 0, count: 64)
        if proc_name(pid, &nb, UInt32(nb.count)) > 0 { return String(cString: nb) }
        return "?"
    }

    /// Returns (name, cpu percent of one core) for processes with activity since the previous
    /// call. The first call primes the baseline and returns [].
    public func usage(now: Double) -> [(String, Double)] {
        var pids = [pid_t](repeating: 0, count: 4096)
        let bytes = proc_listallpids(&pids, Int32(pids.count * MemoryLayout<pid_t>.size))
        let count = Int(bytes) / MemoryLayout<pid_t>.size
        var cur: [pid_t: (cpuNs: UInt64, name: String)] = [:]
        var out: [(String, Double)] = []
        let dt = now - lastT
        for i in 0..<max(count, 0) {
            let pid = pids[i]
            if pid <= 0 { continue }
            var ri = rusage_info_v4()
            let ok = withUnsafeMutablePointer(to: &ri) { p -> Int32 in
                p.withMemoryRebound(to: rusage_info_t?.self, capacity: 1) { proc_pid_rusage(pid, RUSAGE_INFO_V4, $0) }
            }
            if ok != 0 { continue }
            let cpu = ri.ri_user_time + ri.ri_system_time  // ns (mach abs time units, ~ns on Apple Silicon)
            let name = last[pid]?.name ?? Self.name(for: pid)
            cur[pid] = (cpu, name)
            if let prev = last[pid], dt > 0, cpu > prev.cpuNs {
                let pct = Double(cpu - prev.cpuNs) / (dt * 1e9) * 100
                out.append((name, pct))
            }
        }
        last = cur
        lastT = now
        return out
    }
}

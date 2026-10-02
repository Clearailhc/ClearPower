// Estimate per-application power from CPU share x (package power - idle floor).
// Port of daemon/clearpowerd/sources/procs.py (attribution only; enumeration is platform code).
//
// Contract for `usage`: (process name, CPU percent) where a process that keeps one core fully
// busy for the whole window reports 100. That is the psutil convention on Linux
// (`cpu_percent() = 100 * cpuSeconds / (elapsed * logicalCores)`, so one busy core on 16 cores
// is 6.25 % while a fully busy machine sums to 100). macOS libproc rusage and the Windows
// SYSTEM_PROCESS_INFORMATION deltas already deliver that same per-core figure, so all three
// platforms can feed this arithmetic and `sum(cpuPct) / 100` is the busy core count.
//
// The idle floor is subtracted from package power, so what is left is the *dynamic* CPU power,
// and sharing it by busy core-seconds is dimensionally correct on every platform.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClearPower.Core
{
    public sealed class ProcessBudget
    {
        public double IntervalS { get; }
        private readonly double _floorWindowS;
        private double _next;
        private readonly LinkedList<(double t, double w)> _floor = new LinkedList<(double, double)>();
        public List<(string name, double w, double cpuPct)> Top { get; private set; } = new List<(string, double, double)>();

        public ProcessBudget(double intervalS = 2, double floorWindowS = 600)
        {
            IntervalS = intervalS;
            _floorWindowS = floorWindowS;
        }

        /// <summary>
        /// The idle floor is a low percentile of the package power over the window, not its minimum.
        ///
        /// A minimum is pinned by whichever single low reading happened to occur (a screen dimming,
        /// one quiet sample), and it is equally unable to rise again: on a machine that never goes
        /// properly idle the floor is the lowest sample of ten minutes, which can be well under what
        /// "idle plus the usual background" actually costs. A low percentile tracks the quiet end of
        /// the distribution instead of one outlier, so normal background load (a browser, a virus
        /// scanner, the hundreds of processes a Windows desktop runs) does not distort it. With 20 %
        /// of the window's samples below it, twenty seconds of sustained full load cannot move it.
        /// </summary>
        private const int FloorPercentile = 20;

        /// <summary>
        /// Record a package reading and return the floor, or NaN when nothing has been recorded yet.
        /// </summary>
        private double IdleFloor(double now, double packageW)
        {
            _floor.AddLast((now, packageW));
            while (_floor.Count > 0 && now - _floor.First!.Value.t > _floorWindowS) _floor.RemoveFirst();
            if (_floor.Count == 0) return double.NaN;
            var sorted = _floor.Select(p => p.w).OrderBy(w => w).ToArray();
            var idx = Math.Min(sorted.Length - 1, sorted.Length * FloorPercentile / 100);
            return sorted[idx];
        }

        /// <summary>
        /// `usage` yields (process name, cpu percent) pairs; evaluated at most every IntervalS.
        /// `busyCores` is how many cores the machine has been busy for over the window (0.4 means
        /// 40 % of one core); it is accepted for the caller's convenience and diagnostics.
        ///
        /// An unknown package power (-1: Energy Meter/RAPL not ready yet, a counter read failure, or
        /// the first sample after resume from sleep) must never reach the floor: recording -1 as 0 W
        /// would drag the floor to nothing and turn every later budget into the full package power.
        /// Such a sample is skipped outright - the previous result is returned and the interval is
        /// not consumed, so the next tick tries again.
        /// </summary>
        public List<(string name, double w, double cpuPct)> Sample(double now, double packageW, double busyCores, Func<IEnumerable<(string name, double cpuPct)>> usage, int n = 3)
        {
            if (now < _next) return Top;
            if (packageW < 0) return Top;          // unknown power: leave both the floor and the interval alone
            var floor = IdleFloor(now, packageW);
            if (double.IsNaN(floor)) return Top;   // nothing recorded yet: same, retry next tick
            _next = now + IntervalS;
            var budget = Math.Max(packageW - floor, 0.0);
            var agg = new Dictionary<string, double>();
            var order = new List<string>();
            var total = 0.0;
            foreach (var (name, c) in usage())
            {
                if (c <= 0) continue;
                total += c;
                var key = string.IsNullOrEmpty(name) ? "?" : name;
                if (!agg.ContainsKey(key)) { agg[key] = 0; order.Add(key); }
                agg[key] += c;
            }
            var top = new List<(string, double, double)>();
            if (total > 0)
            {
                // Counter.most_common: descending count, insertion order for ties.
                foreach (var key in order.OrderByDescending(k => agg[k]).Take(n))
                    top.Add((key, budget * agg[key] / total, agg[key]));
            }
            Top = top;
            return top;
        }
    }
}

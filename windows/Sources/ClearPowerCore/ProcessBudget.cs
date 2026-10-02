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

        private double IdleFloor(double now, double packageW)
        {
            _floor.AddLast((now, packageW));
            while (_floor.Count > 0 && now - _floor.First!.Value.t > _floorWindowS) _floor.RemoveFirst();
            return _floor.Min(p => p.w);
        }

        /// <summary>
        /// `usage` yields (process name, cpu percent) pairs; evaluated at most every IntervalS.
        ///
        /// An unknown package power (-1: Energy Meter/RAPL not ready yet, a counter read failure,
        /// or the first sample after resume from sleep) must never reach the floor. The floor is a
        /// sliding *minimum* over ten minutes, so one bogus entry sticks for the whole window and
        /// every later budget is computed against it: recording -1 as 0 W makes the budget the full
        /// package power, and recording a re-scaled near-zero reading as the floor makes it
        /// (near) zero, which hides every application. An unusable sample is therefore skipped
        /// outright: the previous result is returned and the interval is not consumed, so the very
        /// next tick samples again instead of waiting out a full interval for nothing.
        /// </summary>
        public List<(string name, double w, double cpuPct)> Sample(double now, double packageW, Func<IEnumerable<(string name, double cpuPct)>> usage, int n = 3)
        {
            if (now < _next) return Top;
            if (packageW < 0) return Top;          // unknown power: leave both the floor and the interval alone
            _next = now + IntervalS;
            var floor = IdleFloor(now, packageW);
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

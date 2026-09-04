// Estimate per-application power from CPU share x (package power - idle floor).
// Port of daemon/clearpowerd/sources/procs.py (attribution only; enumeration is platform code).
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

        /// <summary>`usage` yields (process name, cpu percent) pairs; evaluated at most every IntervalS.</summary>
        public List<(string name, double w, double cpuPct)> Sample(double now, double packageW, Func<IEnumerable<(string name, double cpuPct)>> usage, int n = 3)
        {
            if (now < _next) return Top;
            _next = now + IntervalS;
            var floor = packageW >= 0 ? IdleFloor(now, packageW) : 0.0;
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

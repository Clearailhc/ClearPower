// Battery runtime / time-to-limit estimates from the battery's own energy counter.
// Port of daemon/clearpowerd/runtime.py.
//
// A window of W seconds is evaluated over the current uninterrupted discharging (or
// charging) segment: avg_w = dE/dt. This integrates everything the machine drew, is
// immune to power_now's slow EC updates, and only moves as fast as the window.
using System;
using System.Collections.Generic;

namespace ClearPower.Core
{
    public sealed class RuntimeEstimator
    {
        public static readonly int[] Windows = { 600, 1800, 3600 };
        public const int MinBasisS = 60;
        private const int MaxLen = 3700;

        private readonly LinkedList<(double t, double e, string phase)> _buf = new LinkedList<(double, double, string)>();

        private static string Phase(string status)
        {
            if (status == "Discharging") return "dis";
            if (status == "Charging") return "chg";
            return "idle";
        }

        public void Add(double t, double energyWh, string status)
        {
            _buf.AddLast((t, energyWh, Phase(status)));
            while (_buf.Count > MaxLen) _buf.RemoveFirst();
        }

        /// <summary>Forget the current segment (e.g. after resume from sleep).</summary>
        public void Clear() => _buf.Clear();

        private (double t, double e)? OldestInSegment(double windowS)
        {
            if (_buf.Count == 0) return null;
            var (tNow, _, phase) = _buf.Last!.Value;
            (double, double)? oldest = null;
            for (var node = _buf.Last; node != null; node = node.Previous)
            {
                var (t, e, p) = node.Value;
                if (p != phase || t < tNow - windowS) break;
                oldest = (t, e);
            }
            return oldest;
        }

        /// <summary>Returns snapshot keys runtime_min_*, eta_min_*, runtime_basis_s.</summary>
        public Dictionary<string, object?> Estimate(double energyNowWh, double targetWh, double fallbackW)
        {
            var outp = new Dictionary<string, object?> { ["runtime_basis_s"] = 0 };
            if (_buf.Count == 0)
            {
                foreach (var w in Windows)
                {
                    outp[$"runtime_min_{w / 60}"] = -1.0;
                    outp[$"eta_min_{w / 60}"] = -1.0;
                }
                return outp;
            }
            var (tNow, eLast, phase) = _buf.Last!.Value;
            var eNow = energyNowWh != 0 ? energyNowWh : eLast;
            foreach (var w in Windows)
            {
                var key = w / 60;
                double runtime = -1, eta = -1;
                var old = OldestInSegment(w);
                double? avgW = null;
                int basis = 0;
                if (old != null)
                {
                    var dt = tNow - old.Value.t;
                    var de = old.Value.e - eNow;  // positive when discharging
                    if (dt >= MinBasisS && Math.Abs(de) > 1e-3)
                    {
                        avgW = de / dt * 3600.0;
                        basis = (int)dt;
                    }
                }
                if (phase == "dis")
                {
                    double? p = (avgW != null && avgW > 0.3) ? avgW : (fallbackW > 0.3 ? fallbackW : (double?)null);
                    if (p != null) runtime = eNow / p.Value * 60.0;
                }
                else if (phase == "chg")
                {
                    double? p = (avgW != null && avgW < -0.3) ? -avgW : (fallbackW > 0.3 ? fallbackW : (double?)null);
                    if (p != null && targetWh > eNow) eta = (targetWh - eNow) / p.Value * 60.0;
                }
                outp[$"runtime_min_{key}"] = runtime;
                outp[$"eta_min_{key}"] = eta;
                var cur = (int)outp["runtime_basis_s"]!;
                var contrib = ((runtime > 0 || eta > 0) && avgW != null && avgW != 0) ? basis : 0;
                outp["runtime_basis_s"] = Math.Max(cur, contrib);
            }
            return outp;
        }
    }
}

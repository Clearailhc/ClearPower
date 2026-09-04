// Intel RAPL through the in-box Energy Meter Interface (Windows 11), surfaced as the
// "Energy Meter" performance counter set: instances RAPL_Package<n>_{PKG,PP0,PP1,DRAM},
// counters Energy (cumulative picowatt-hours), Time (ms) and Power (mW).
// Port of daemon/clearpowerd/sources/rapl.py: watts are computed from our own deltas
// of the raw Energy/Time counters, never from the cooked Power value.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClearPower.Win
{
    public sealed class EnergyReading
    {
        public double Package = -1, Core = -1, Uncore = -1, Dram = -1;
    }

    public sealed class EnergySource
    {
        private const string Category = "Energy Meter";
        private static readonly Regex InstanceRe = new Regex(@"^RAPL_Package(\d+)_(PKG|PP0|PP1|DRAM)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class Channel
        {
            public string Domain = "";
            public PerformanceCounter Energy = null!;
            public PerformanceCounter Time = null!;
            public long LastEnergy;
            public long LastTime;
            public bool Primed;
        }

        private readonly List<Channel> _channels = new List<Channel>();
        private double _nextInitTry;
        public bool Available { get; private set; }
        public Action<string> Log { get; set; } = _ => { };

        public EnergySource()
        {
            TryInit();
        }

        private void TryInit()
        {
            _nextInitTry = Clock.MonotonicNow() + 10;
            try
            {
                if (!PerformanceCounterCategory.Exists(Category)) return;
                var cat = new PerformanceCounterCategory(Category);
                foreach (var inst in cat.GetInstanceNames())
                {
                    var m = InstanceRe.Match(inst);
                    if (!m.Success) continue;
                    var domain = m.Groups[2].Value.ToUpperInvariant() switch
                    {
                        "PKG" => "package",
                        "PP0" => "core",
                        "PP1" => "uncore",
                        "DRAM" => "dram",
                        _ => "",
                    };
                    if (domain == "") continue;
                    _channels.Add(new Channel
                    {
                        Domain = domain,
                        Energy = new PerformanceCounter(Category, "Energy", inst, readOnly: true),
                        Time = new PerformanceCounter(Category, "Time", inst, readOnly: true),
                    });
                }
                Available = _channels.Count > 0;
                if (Available) Log($"energy meter: {_channels.Count} RAPL channels");
            }
            catch (Exception e)
            {
                Log($"energy meter unavailable: {e.Message}");
                Available = false;
            }
        }

        private EnergyReading? _lastResult;
        private const long MinDeltaMs = 200;

        /// <summary>
        /// Per-domain watts since the previous call, or null when no delta is available yet.
        /// Sampled again within 200 ms (popover just opened, rate change): the previous
        /// reading is returned and the baselines are kept, so a real delta accumulates.
        /// </summary>
        public EnergyReading? Sample()
        {
            if (!Available)
            {
                if (Clock.MonotonicNow() >= _nextInitTry) TryInit();
                if (!Available) return null;
            }
            var readings = new List<(Channel ch, long e, long t)>();
            foreach (var ch in _channels)
            {
                try
                {
                    readings.Add((ch, ch.Energy.NextSample().RawValue, ch.Time.NextSample().RawValue));
                }
                catch (Exception ex)
                {
                    Log($"energy meter read failed: {ex.Message}");
                    Available = false;
                    _channels.Clear();
                    return null;
                }
            }
            if (readings.Count > 0 && readings.All(r => r.ch.Primed) && readings.All(r => r.t - r.ch.LastTime < MinDeltaMs && r.t - r.ch.LastTime >= 0))
                return _lastResult;   // too soon after the previous sample: keep the previous reading
            var sums = new Dictionary<string, double>();
            foreach (var (ch, e, t) in readings)
            {
                if (ch.Primed)
                {
                    var dt = t - ch.LastTime;   // ms
                    var de = e - ch.LastEnergy; // pWh
                    // Skip the first sample, a non-advancing clock, resume-from-sleep gaps and counter resets.
                    if (dt > 0 && dt <= 10_000 && de >= 0)
                    {
                        var w = de * 3.6e-6 / dt;   // pWh -> J: *3.6e-9; / (dt ms / 1000)
                        sums[ch.Domain] = sums.TryGetValue(ch.Domain, out var s) ? s + w : w;
                    }
                }
                ch.LastEnergy = e;
                ch.LastTime = t;
                ch.Primed = true;
            }
            if (sums.Count == 0) { _lastResult = null; return null; }
            var r = new EnergyReading();
            if (sums.TryGetValue("package", out var p)) r.Package = p;
            if (sums.TryGetValue("core", out var c)) r.Core = c;
            if (sums.TryGetValue("uncore", out var u)) r.Uncore = u;
            if (sums.TryGetValue("dram", out var d)) r.Dram = d;
            _lastResult = r;
            return r;
        }

        /// <summary>Drop the delta baselines (after resume from sleep).</summary>
        public void Reset()
        {
            foreach (var ch in _channels) ch.Primed = false;
            _lastResult = null;
        }
    }
}

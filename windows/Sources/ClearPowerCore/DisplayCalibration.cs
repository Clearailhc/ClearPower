// Display power calibration for panels without a power sensor.
// Port of daemon/clearpowerd/display_cal.py (mirrors the Swift port).
//
// The only knobs we control are brightness and (indirectly) picture content; the only
// truth we have is platform power minus the measured SoC and memory, i.e. "everything
// else". Sweeping brightness while the machine is idle yields the panel's emission per
// level. For OLED the emission also scales with the average picture luminance, so the
// table is normalised to a white screen and re-scaled at runtime with the live content
// level supplied by the UI.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClearPower.Core
{
    public interface IBrightnessControl
    {
        bool Available { get; }
        /// <summary>Raw brightness range is 0...Max (Windows: 0...100).</summary>
        int Max { get; }
        int? ReadRaw();
        void SetRaw(int value);
    }

    public sealed class DisplayCalibration
    {
        public static readonly double[] Levels = { 0.0, 0.01, 0.1, 0.25, 0.5, 0.75, 1.0 };
        /// <summary>Seconds to wait after a brightness change before sampling (Linux/macOS: 1.5).</summary>
        public double SettleS { get; set; } = 1.5;
        public int Samples { get; set; } = 5;
        public double SampleGapS { get; set; } = 1.0;

        public string State { get; private set; } = "idle";  // idle | running | done | failed
        public double Progress { get; private set; }
        public string Message { get; private set; } = "";
        public List<(int raw, double w)> Table { get; private set; } = new List<(int, double)>();
        public double AplCal { get; private set; } = -1;
        public double AplMeasured { get; private set; } = -1;
        public double Rest0 { get; private set; } = -1;
        public double CalibratedAt { get; private set; }
        public double Apl { get; private set; } = -1;
        private double _aplTs;
        private readonly IBrightnessControl _bl;
        private readonly string? _path;
        public Action<string> Log { get; set; } = _ => { };

        private sealed class Run
        {
            public int Orig;
            public int Idx;
            public double PhaseT;
            public int LevelRaw;
            public double LastSampleT;
            public List<double> Samples = new List<double>();
            public List<(int raw, double rest)> Results = new List<(int, double)>();
            public List<double> Apls = new List<double>();
        }
        private Run? _run;

        public DisplayCalibration(IBrightnessControl brightness, string? storagePath)
        {
            _bl = brightness;
            _path = storagePath;
            Load();
        }

        public bool Calibrated => Table.Count >= 2;

        // ---- persistence ----
        private void Load()
        {
            if (_path == null || !File.Exists(_path)) return;
            try
            {
                var d = Json.ParseObject(File.ReadAllText(_path));
                if (!(d.TryGetValue("table", out var tv) && tv is List<object?> t)) return;
                Table = t.Select(p => p as List<object?>)
                         .Where(p => p != null && p!.Count == 2 && Snapshot.Double(p[0]) != null && Snapshot.Double(p[1]) != null)
                         .Select(p => ((int)Snapshot.Double(p![0])!.Value, Snapshot.Double(p[1])!.Value))
                         .ToList();
                AplCal = 1.0;  // table is normalised to a white screen (see Finish)
                Rest0 = d.D("rest0", -1);
                CalibratedAt = d.D("calibrated_at", 0);
                State = Table.Count == 0 ? "idle" : "done";
            }
            catch (Exception e)
            {
                Log($"cannot load calibration: {e.Message}");
            }
        }

        private void Save()
        {
            if (_path == null) return;
            var obj = new Dictionary<string, object?>
            {
                ["table"] = Table.Select(p => new List<object?> { p.raw, p.w }).ToList(),
                ["apl_cal"] = AplCal, ["apl_measured"] = AplMeasured,
                ["rest0"] = Rest0, ["calibrated_at"] = CalibratedAt, ["max_brightness"] = _bl.Max,
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, Json.Serialize(obj));
            }
            catch (Exception e)
            {
                Log($"cannot save calibration: {e.Message}");
            }
        }

        /// <summary>Test/seed hook: install a table directly.</summary>
        public void SetTable(List<(int, double)> t, double rest0 = -1)
        {
            Table = t;
            AplCal = 1.0;
            Rest0 = rest0;
            State = Calibrated ? "done" : "idle";
        }

        // ---- content level from the UI ----
        public void SetContent(double value, double now)
        {
            Apl = Math.Max(0, Math.Min(1, value));
            _aplTs = now;
            if (State == "running" && _run != null) _run.Apls.Add(Apl);
        }

        private double FreshApl(double now) => (Apl >= 0 && now - _aplTs < 60) ? Apl : -1;

        // ---- runtime estimate ----
        public double EmissionW(int? rawBrightness, double now)
        {
            if (!Calibrated || rawBrightness == null) return -1;
            var rb = rawBrightness.Value;
            var pts = Table;
            double e;
            if (rb <= pts[0].raw) e = pts[0].w;
            else if (rb >= pts[pts.Count - 1].raw) e = pts[pts.Count - 1].w;
            else
            {
                e = pts[pts.Count - 1].w;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var (b0, e0) = pts[i];
                    var (b1, e1) = pts[i + 1];
                    if (b0 <= rb && rb <= b1)
                    {
                        e = e0 + (e1 - e0) * (rb - b0) / (double)Math.Max(b1 - b0, 1);
                        break;
                    }
                }
            }
            var a = FreshApl(now);
            if (a >= 0 && AplCal > 0.02) e *= Math.Max(0.02, Math.Min(1.2, a / AplCal));
            return e;
        }

        // ---- state machine ----
        public void Start(double now, bool energyOK)
        {
            if (State == "running") return;
            if (!_bl.Available) { State = "failed"; Message = "no backlight device"; return; }
            if (!energyOK) { State = "failed"; Message = "SoC energy counters not readable; cannot isolate the display"; return; }
            var orig = _bl.ReadRaw() ?? _bl.Max;
            _run = new Run { Orig = orig, PhaseT = now };
            State = "running"; Progress = 0; Message = "";
            ApplyLevel(now);
            Log($"display calibration started (orig brightness {orig})");
        }

        /// <summary>Platform precondition not met (e.g. Windows needs battery power): report without starting.</summary>
        public void Refuse(string message)
        {
            if (State == "running") return;
            State = Calibrated ? "done" : "failed";
            Message = message;
        }

        private void ApplyLevel(double now)
        {
            var r = _run;
            if (r == null) return;
            var raw = (int)Math.Round(Levels[r.Idx] * _bl.Max, MidpointRounding.AwayFromZero);
            try
            {
                _bl.SetRaw(raw);
            }
            catch (Exception e)
            {
                Finish($"cannot set brightness: {e.Message}");
                return;
            }
            r.LevelRaw = raw; r.PhaseT = now; r.Samples = new List<double>(); r.LastSampleT = 0;
        }

        public void Tick(double restRawW, double now)
        {
            if (State != "running" || _run == null) return;
            var r = _run;
            if (now - r.PhaseT < SettleS) return;
            if (restRawW < 0) { Finish("platform power unavailable"); return; }
            if (now - r.LastSampleT >= SampleGapS)
            {
                r.Samples.Add(restRawW);
                r.LastSampleT = now;
            }
            Progress = (r.Idx + r.Samples.Count / (double)Samples) / Levels.Length;
            if (r.Samples.Count >= Samples)
            {
                r.Results.Add((r.LevelRaw, Median(r.Samples)));
                r.Idx += 1;
                if (r.Idx >= Levels.Length) Finish();
                else ApplyLevel(now);
            }
        }

        public void Cancel()
        {
            if (State == "running") Finish("cancelled");
        }

        public static double Median(List<double> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            var n = s.Count;
            if (n == 0) return -1;
            return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
        }

        private void Finish(string? failed = null)
        {
            var r = _run;
            if (r != null)
            {
                try { _bl.SetRaw(r.Orig); } catch (Exception e) { Log($"could not restore brightness: {e.Message}"); }
            }
            if (failed != null)
            {
                State = Calibrated ? "done" : "failed";
                Message = failed;
                Log($"display calibration aborted: {failed}");
            }
            else if (r != null)
            {
                var res = r.Results.OrderBy(p => p.raw).ToList();
                var r0 = res[0].rest;
                var t = new List<(int, double)>();
                var runningMax = 0.0;
                foreach (var (raw, rest) in res)
                {
                    runningMax = Math.Max(runningMax, rest - r0);  // emission can only grow with brightness
                    t.Add((raw, Math.Round(runningMax * 1000, MidpointRounding.AwayFromZero) / 1000));
                }
                Table = t;
                Rest0 = Math.Round(r0 * 1000, MidpointRounding.AwayFromZero) / 1000;
                // The UI shows a white screen during the sweep, so by construction the table is
                // the emission for content level 1.0. The measured level is only a sanity check.
                var apls = r.Apls;
                var measured = apls.Count >= 2 ? Median(apls.Skip(apls.Count / 3).ToList()) : -1;
                if (measured >= 0 && measured < 0.8)
                    Log($"calibration screen content level was {measured:F2} (expected ~1.0)");
                AplCal = 1.0;
                AplMeasured = measured;
                CalibratedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                State = "done"; Progress = 1; Message = "";
                Save();
                Log($"display calibration done: {string.Join(", ", t.Select(p => $"({p.Item1}, {p.Item2})"))} (rest0 {Rest0})");
            }
            _run = null;
        }

        public Dictionary<string, object?> SnapshotKeys => new Dictionary<string, object?>
        {
            ["display_calibrated"] = Calibrated,
            ["calib_state"] = State,
            ["calib_progress"] = Progress,
            ["calib_message"] = Message,
            ["calibrated_at"] = CalibratedAt,
            ["content_apl"] = Apl,
        };
    }
}

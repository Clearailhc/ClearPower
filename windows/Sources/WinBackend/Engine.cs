// The sampling loop: sampler + charge state + history + runtime estimate + display
// calibration + top processes. Mirrors tick() in daemon/clearpowerd/__main__.py and
// macos/Sources/MacBackend/Engine.swift. Runs inside the tray app on its own thread;
// everything is unprivileged on Windows, including charge control (Lenovo Power Manager RPC).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ClearPower.Core;

namespace ClearPower.Win
{
    public sealed class EngineConfig
    {
        public int SampleIntervalMs = 1000;     // while a client is watching (popover open)
        public int IdleIntervalMs = 2000;       // nobody asked for details in the last HotSeconds
        public double HotSeconds = 6;
        public double ProcsIntervalS = 3;
        public double SmoothingS = 5.0;
        public int HistorySeconds = 24 * 3600;
        public int HistoryStepS = 10;
        public int DischargeFloorPct = 20;
        // Windows: the battery's Rate is refreshed by the EC every second or two, so calibration
        // waits longer per brightness level than on Linux (1.5 s / 1 s).
        public double CalibSettleS = 3.0;
        public double CalibSampleGapS = 2.0;
    }

    public static class Paths
    {
        public static string StateDir
        {
            get
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClearPower");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string DisplayCalPath => Path.Combine(StateDir, "display_cal.json");
        public static string StatePath => Path.Combine(StateDir, "state.json");
        public static string SettingsPath => Path.Combine(StateDir, "settings.json");
        public static string LogPath => Path.Combine(StateDir, "clearpower.log");
    }

    public sealed class Engine : IDisposable
    {
        public EngineConfig Cfg { get; }
        public Sampler Sampler { get; }
        public DisplayCalibration DisplayCal { get; }
        public RuntimeEstimator Runtime { get; private set; } = new RuntimeEstimator();
        public History History { get; }
        public ChargeStateMachine Charge { get; }
        public IChargeHardware ChargeHardware { get; }
        private readonly ProcessBudget _procs;
        private readonly ProcessSource _procSource = new ProcessSource();
        public List<(string name, double w, double cpuPct)> TopProcesses { get; private set; } = new List<(string, double, double)>();
        public Dictionary<string, object?> Snapshot { get; private set; } = new Dictionary<string, object?>();
        private double _lastClientActivity = -1e9;
        private readonly object _gate = new object();
        private Thread? _thread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private volatile bool _running;
        /// <summary>Raised on the engine thread after every tick.</summary>
        public event Action<Dictionary<string, object?>>? Sample;
        /// <summary>Raised on the engine thread when the charge state changed (mode/limit/target).</summary>
        public event Action? ChargeStateChanged;
        public Action<string> Log { get; set; } = s => Console.Error.WriteLine(s);

        public Engine(EngineConfig? config = null, IChargeHardware? chargeHardware = null)
        {
            Cfg = config ?? new EngineConfig();
            var brightness = new Brightness();
            DisplayCal = new DisplayCalibration(brightness, Paths.DisplayCalPath)
            {
                SettleS = Cfg.CalibSettleS,
                SampleGapS = Cfg.CalibSampleGapS,
            };
            Sampler = new Sampler(Cfg.SmoothingS, DisplayCal);
            History = new History(Cfg.HistorySeconds, Cfg.HistoryStepS);
            _procs = new ProcessBudget(Cfg.ProcsIntervalS);
            ChargeHardware = chargeHardware ?? new NullChargeHardware();
            Charge = new ChargeStateMachine(ChargeHardware, Cfg.DischargeFloorPct);
            DisplayCal.Log = s => Log(s);
            Sampler.Log = s => Log(s);
        }

        /// <summary>A client asked for details (popover open): sample at full rate for HotSeconds.</summary>
        public void Touch() => _lastClientActivity = Clock.MonotonicNow();
        public bool Hot => Clock.MonotonicNow() - _lastClientActivity < Cfg.HotSeconds;

        public Dictionary<string, object?> ChargeState()
        {
            var st = Charge.State;
            st["charge_control_supported"] = Charge.Supported;
            st["discharge_supported"] = Charge.DischargeSupported;
            if (ChargeHardware is IChargeHardwareInfo info) st.MergeFrom(info.ExtraState());
            return st;
        }

        public Dictionary<string, object?> Tick()
        {
            lock (_gate)
            {
                var hot = Hot;
                var snap = Sampler.Sample(hot);
                var now = Clock.MonotonicNow();
                var ended = Charge.Tick(snap.I("bat_pct", 0), snap.S("bat_status"));
                snap.MergeFrom(ChargeState());
                Runtime.Add(now, snap.D("bat_energy_wh", 0), snap.S("bat_status"));
                var limit = snap.I("charge_limit", 100);
                var targetWh = snap.D("bat_full_wh", 0) * limit / 100.0;
                snap.MergeFrom(Runtime.Estimate(snap.D("bat_energy_wh", 0), targetWh, Math.Abs(snap.D("bat_w", 0))));
                if (DisplayCal.State == "running" && snap.B("on_ac"))
                    DisplayCal.Cancel();  // the sweep needs the battery as the whole-machine sensor
                DisplayCal.Tick(Sampler.CalibrationRest, now);
                snap.MergeFrom(DisplayCal.SnapshotKeys);
                History.Add(snap);
                if (hot)
                    TopProcesses = _procs.Sample(now, snap.D("package_w"), () => _procSource.Usage(now));
                Snapshot = snap;
                if (ended) ChargeStateChanged?.Invoke();
                Sample?.Invoke(snap);
                return snap;
            }
        }

        private int DesiredInterval()
        {
            var busy = Hot || Charge.Mode != ChargeMode.Limit || DisplayCal.State == "running";
            return busy ? Cfg.SampleIntervalMs : Cfg.IdleIntervalMs;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            Charge.ApplyStartup();
            _thread = new Thread(Loop) { IsBackground = true, Name = "clearpower-engine" };
            _thread.Start();
        }

        private void Loop()
        {
            while (_running)
            {
                try { Tick(); }
                catch (Exception e) { Log($"tick failed: {e}"); }
                _wake.WaitOne(DesiredInterval());
            }
        }

        /// <summary>Sample sooner than scheduled (e.g. the popover just opened).</summary>
        public void Poke() => _wake.Set();

        public void Stop()
        {
            _running = false;
            _wake.Set();
            _thread?.Join(3000);
            _thread = null;
            lock (_gate) Charge.Shutdown();
        }

        public void OnResume()
        {
            lock (_gate)
            {
                Sampler.OnResume();
                Runtime.Clear();
                if (ChargeHardware is IChargeHardwareInfo info) info.Reassert();
            }
        }

        // ---- methods (the D-Bus / helper surface) ----
        public void SetChargeLimit(int pct) { lock (_gate) { Charge.SetLimit(pct); } ChargeStateChanged?.Invoke(); }
        public void StartTopUp() { lock (_gate) { Charge.StartTopUp(); } ChargeStateChanged?.Invoke(); }
        public void StartDischarge(int target) { lock (_gate) { Charge.StartDischarge(target); } ChargeStateChanged?.Invoke(); }
        public void CancelSpecial() { lock (_gate) { Charge.Cancel(); } ChargeStateChanged?.Invoke(); }

        /// <summary>Returns false (with a message) when the platform precondition is not met.</summary>
        public bool CalibrateDisplay(out string message)
        {
            lock (_gate)
            {
                message = "";
                if (Snapshot.B("on_ac", true) || Snapshot.D("bat_w", 0) >= -0.05)
                {
                    message = I18n.T("calibNeedsBattery");
                    DisplayCal.Refuse(message);
                    return false;
                }
                DisplayCal.Start(Clock.MonotonicNow(), Sampler.EnergyAvailable);
                if (DisplayCal.State == "failed") { message = DisplayCal.Message; return false; }
                Poke();
                return true;
            }
        }

        public void CancelCalibration() { lock (_gate) DisplayCal.Cancel(); }

        public void SetDisplayContent(double apl)
        {
            Touch();
            lock (_gate) DisplayCal.SetContent(apl, Clock.MonotonicNow());
        }

        public List<(string name, double w, double cpuPct)> GetTopProcesses(int n)
        {
            Touch();
            lock (_gate) return TopProcesses.GetRange(0, Math.Min(n, TopProcesses.Count));
        }

        public List<(double t, double v)> GetHistory(string field, double seconds)
        {
            lock (_gate) return History.Get(field, seconds);
        }

        public void Dispose()
        {
            Stop();
            Sampler.Battery.Dispose();
        }
    }

    /// <summary>Optional extras a charge hardware can report into the snapshot (thresholds, method).</summary>
    public interface IChargeHardwareInfo
    {
        Dictionary<string, object?> ExtraState();
        /// <summary>Re-apply the current thresholds (after resume; some firmware forgets).</summary>
        void Reassert();
    }

    /// <summary>Persisted charge limit shared by all hardware backends (%LOCALAPPDATA%\ClearPower\state.json).</summary>
    public static class LimitStore
    {
        public static int? Load()
        {
            try
            {
                if (!File.Exists(Paths.StatePath)) return null;
                var d = Json.ParseObject(File.ReadAllText(Paths.StatePath));
                var v = d.I("limit", -1);
                return v > 0 ? v : (int?)null;
            }
            catch (Exception) { return null; }
        }

        public static void Save(int limit)
        {
            try
            {
                File.WriteAllText(Paths.StatePath, Json.Serialize(new Dictionary<string, object?> { ["limit"] = limit }));
            }
            catch (Exception) { }
        }
    }

    /// <summary>No vendor interface: charge controls hidden, exactly like unsupported Linux hardware.</summary>
    public sealed class NullChargeHardware : IChargeHardware
    {
        public bool ThresholdsSupported => false;
        public IReadOnlyList<string> Behaviours => new[] { "auto" };
        public void WriteThresholds(int start, int end) => throw new ChargeException(95, "charge thresholds not supported");
        public void WriteBehaviour(string behaviour) { }
        public int? LoadLimit() => LimitStore.Load();
        public void SaveLimit(int limit) => LimitStore.Save(limit);
    }
}

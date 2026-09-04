// Collect all Windows sources into one snapshot dict. Mirrors daemon/clearpowerd/sampler.py
// and macos/Sources/MacBackend/Sampler.swift: the arithmetic lives in ClearPowerCore.PowerModel;
// this file only gathers inputs.
//
// Windows has no platform power sensor (RAPL psys is not exposed by the Energy Meter
// Interface). On battery the whole-machine draw is the battery's own reading, exactly as on
// Linux. On AC, once the display has been calibrated (which on Windows must happen on
// battery), the platform total is synthesised from the measured SoC + memory plus the
// peripheral baseline and panel emission learnt during calibration, and flagged
// sys_source = "estimate" so the UI marks it ≈.
using System;
using System.Collections.Generic;
using ClearPower.Core;

namespace ClearPower.Win
{
    public sealed class Sampler
    {
        public BatterySource Battery { get; } = new BatterySource();
        public EnergySource Energy { get; } = new EnergySource();
        public Brightness Brightness { get; } = new Brightness();
        private readonly PowerModel _model;
        public DisplayCalibration? DisplayCal { get; }
        public Dictionary<string, object?> Last { get; private set; } = new Dictionary<string, object?>();
        public RawPower Raw => _model.Raw;
        /// <summary>Platform power minus SoC and memory, for display calibration; -1 unless on battery.</summary>
        public double CalibrationRest { get; private set; } = -1;
        public bool EnergyAvailable => Energy.Available;
        public Action<string> Log { get; set; } = _ => { };

        public Sampler(double smoothingS, DisplayCalibration? displayCal)
        {
            _model = new PowerModel(smoothingS);
            DisplayCal = displayCal;
            Battery.Log = s => Log(s);
            Energy.Log = s => Log(s);
            Brightness.Log = s => Log(s);
        }

        public void OnResume()
        {
            Energy.Reset();
            _model.Reset();
            Battery.Invalidate();
        }

        public Dictionary<string, object?> Sample(bool hot = true)
        {
            var now = Clock.MonotonicNow();
            var snap = new Dictionary<string, object?> { ["ts"] = Clock.UnixNow() };
            var bat = Battery.Read();
            var bl = Brightness.Read();
            var e = Energy.Sample();
            snap.MergeFrom(bat);
            snap.MergeFrom(bl);
            snap["temp_cpu"] = -1.0; snap["temp_gpu"] = -1.0; snap["temp_nvme"] = -1.0;
            snap["fan1"] = -1; snap["fan2"] = -1;
            var onAC = bat.B("on_ac", true);

            var raw = RawPower.Empty;
            raw.BatW = bat.D("bat_w", 0);
            if (e != null)
            {
                raw.Package = e.Package; raw.Core = e.Core; raw.Uncore = e.Uncore; raw.Dram = e.Dram;
            }
            var brightnessRaw = bl.I("brightness_raw", -1);
            var displayOn = bl.B("display_on", true);
            var emission = DisplayCal?.EmissionW(brightnessRaw >= 0 ? brightnessRaw : (int?)null, now) ?? -1;
            var rest0 = DisplayCal?.Rest0 ?? -1;
            if (onAC && raw.Package >= 0 && DisplayCal != null && DisplayCal.Calibrated && rest0 >= 0)
                raw.Psys = raw.Package + Math.Max(raw.Dram, 0) + rest0 + (displayOn ? Math.Max(emission, 0) : 0);

            var breakdown = _model.Update(raw, onAC, now, emission, displayOn);
            snap.MergeFrom(breakdown);
            if (snap.S("sys_source") == "psys") snap["sys_source"] = "estimate";

            // Battery truth minus SoC and memory is what calibration needs (Linux: psys - package - dram).
            CalibrationRest = (!onAC && raw.BatW < -0.05 && raw.Package >= 0)
                ? Math.Max(-raw.BatW - raw.Package - Math.Max(raw.Dram, 0), 0)
                : -1;

            snap["platform"] = "windows";
            snap["rest0_w"] = rest0;
            snap["platform_profile"] = PowerMode.Read();
            snap["rapl_available"] = Energy.Available && e != null;
            if (DisplayCal != null) snap.MergeFrom(DisplayCal.SnapshotKeys);
            Last = snap;
            return snap;
        }
    }
}

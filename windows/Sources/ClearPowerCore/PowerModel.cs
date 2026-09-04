// Smoothing + conserved power breakdown. Port of the arithmetic in
// daemon/clearpowerd/sampler.py (everything except the Linux sources); mirrors
// macos/Sources/ClearPowerCore/PowerModel.swift.
//
// Every watt shown to the user is either measured (battery, platform total, per-block
// energy counters) or derived from measured values by subtraction, so the parts sum to
// the whole by construction. Inputs are smoothed (EMA) before the breakdown so the
// breakdown of smoothed values is still conserved. `Raw` keeps the unsmoothed inputs
// for the runtime estimator and display calibration.
using System;
using System.Collections.Generic;

namespace ClearPower.Core
{
    /// <summary>
    /// Raw, unsmoothed inputs. -1 = unavailable (except BatW, which is signed and defaults to 0).
    /// Domain names follow the Linux/RAPL shape so the maths is shared:
    ///   psys    whole-platform power (Linux: RAPL psys; macOS: SMC PSTR; Windows: synthesised on AC)
    ///   package SoC total           (RAPL package / EMI RAPL_Package0_PKG)
    ///   core    CPU                 (RAPL core / PP0)
    ///   uncore  GPU                 (RAPL uncore / PP1)
    ///   dram    memory              (RAPL dram / DRAM)
    /// soc = package - core - uncore (fabric, NPU, media, memory controller).
    /// </summary>
    public struct RawPower
    {
        public double BatW;
        public double Psys;
        public double Package;
        public double Core;
        public double Uncore;
        public double Dram;

        public static RawPower Empty => new RawPower { BatW = 0, Psys = -1, Package = -1, Core = -1, Uncore = -1, Dram = -1 };

        public RawPower(double batW, double psys, double package, double core, double uncore, double dram)
        {
            BatW = batW; Psys = psys; Package = package; Core = core; Uncore = uncore; Dram = dram;
        }

        /// <summary>Platform power not attributable to the SoC or memory (display + peripherals).</summary>
        public double Rest
        {
            get
            {
                if (Psys < 0) return -1;
                return Math.Max(Psys - Math.Max(Package, 0) - Math.Max(Dram, 0), 0);
            }
        }
    }

    public sealed class PowerModel
    {
        private static readonly string[] Smoothed = { "bat_w", "psys", "package", "core", "uncore", "dram" };
        private readonly Dictionary<string, Ema> _ema = new Dictionary<string, Ema>();
        public RawPower Raw { get; private set; } = RawPower.Empty;

        public PowerModel(double smoothingS)
        {
            foreach (var k in Smoothed) _ema[k] = new Ema(smoothingS);
        }

        /// <summary>Forget the smoothing state (after resume from sleep).</summary>
        public void Reset()
        {
            foreach (var e in _ema.Values) e.Reset();
        }

        private double Smooth(string key, double value, double now)
        {
            if (value < 0)
            {
                _ema[key].Reset();
                return -1;
            }
            return _ema[key].Update(value, now);
        }

        /// <summary>
        /// Returns the breakdown keys of the snapshot. `displayEmission` is the calibrated panel
        /// emission for the current brightness (-1 when uncalibrated), `displayOn` whether the
        /// panel is lit.
        /// </summary>
        public Dictionary<string, object?> Update(RawPower input, bool onAC, double now, double displayEmission, bool displayOn)
        {
            Raw = input;
            // ---- smoothed inputs ----
            // bat_w is signed; the -1 sentinel logic must not apply to it.
            var batW = _ema["bat_w"].Update(input.BatW, now);
            var psys = Smooth("psys", input.Psys, now);
            var package = Smooth("package", input.Package, now);
            var core = Smooth("core", input.Core, now);
            var uncore = Smooth("uncore", input.Uncore, now);
            var dram = Smooth("dram", input.Dram, now);

            // ---- whole-machine draw ----
            double sysW; string sysSource;
            if (!onAC && batW < -0.05)
            {
                sysW = -batW; sysSource = "battery";      // physical truth incl. all losses
            }
            else if (psys > 0)
            {
                sysW = psys; sysSource = "psys";
            }
            else if (package > 0)
            {
                sysW = package + 3.0; sysSource = "estimate";
            }
            else
            {
                sysW = -1; sysSource = "none";
            }

            // ---- breakdown (all derived by subtraction => conserved) ----
            double cpuW = -1, gpuW = -1, socW = -1, memW = -1, restW = -1;
            if (package > 0 && sysW > 0)
            {
                cpuW = Math.Max(core, 0);
                gpuW = Math.Max(uncore, 0);
                socW = Math.Max(package - cpuW - gpuW, 0);
                memW = Math.Max(dram, 0);
                var measured = package + memW;
                if (measured > sysW)  // platform total occasionally undershoots; keep it authoritative
                {
                    var k = sysW / measured;
                    cpuW *= k; gpuW *= k; socW *= k; memW *= k;
                    measured = sysW;
                }
                restW = sysW - measured;
            }
            else if (sysW > 0)
            {
                restW = sysW;
            }

            double displayW = -1;
            var otherW = restW;
            if (restW >= 0 && displayEmission >= 0)
            {
                displayW = displayOn ? Math.Min(displayEmission, restW) : 0;
                otherW = restW - displayW;
            }

            // Adapter supplies the machine (minus whatever the battery is contributing when the
            // adapter is too weak) plus the charge current. Battery draw on AC => two sources.
            var adapterW = (onAC && sysW > 0) ? (sysW - Math.Max(-batW, 0) + Math.Max(batW, 0)) : 0;
            adapterW = Math.Max(adapterW, 0);

            return new Dictionary<string, object?>
            {
                ["sys_w"] = sysW, ["sys_source"] = sysSource,
                ["psys_w"] = psys, ["package_w"] = package,
                ["cpu_w"] = cpuW, ["gpu_w"] = gpuW, ["soc_w"] = socW, ["mem_w"] = memW,
                ["rest_w"] = restW, ["display_w"] = displayW, ["other_w"] = otherW,
                ["bat_w"] = batW, ["bat_w_raw"] = input.BatW,
                ["adapter_w"] = adapterW,
            };
        }
    }
}

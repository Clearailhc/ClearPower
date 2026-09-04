// Charge control state machine: limit (normal) / topup / discharge.
// Port of the policy half of daemon/clearpowerd/charge_control.py; the hardware half
// is behind IChargeHardware so Linux thresholds, macOS SMC keys and the Windows vendor
// interface share one machine. Mirrors macos/Sources/ClearPowerCore/ChargeStateMachine.swift.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClearPower.Core
{
    public enum ChargeMode { Limit, Topup, Discharge }

    public static class ChargeModeExt
    {
        public static string Raw(this ChargeMode m) => m switch
        {
            ChargeMode.Topup => "topup",
            ChargeMode.Discharge => "discharge",
            _ => "limit",
        };
    }

    public sealed class ChargeException : Exception
    {
        public int Errno { get; }
        public ChargeException(int errno, string message) : base(message) { Errno = errno; }
        public override string ToString() => $"{Message} (errno {Errno})";
    }

    /// <summary>
    /// What the platform can do. Behaviours use the Linux `charge_behaviour` vocabulary:
    /// "auto", "inhibit-charge", "force-discharge".
    /// </summary>
    public interface IChargeHardware
    {
        bool ThresholdsSupported { get; }
        IReadOnlyList<string> Behaviours { get; }
        /// <summary>Charging stops at `end` and resumes below `start`.</summary>
        void WriteThresholds(int start, int end);
        void WriteBehaviour(string behaviour);
        /// <summary>Persisted limit (null = none saved). Only the limit survives restarts.</summary>
        int? LoadLimit();
        void SaveLimit(int limit);
    }

    public sealed class ChargeStateMachine
    {
        public ChargeMode Mode { get; private set; } = ChargeMode.Limit;
        public int Target { get; private set; }
        public int Limit { get; private set; } = 100;
        public int Floor { get; }
        private readonly IChargeHardware _hw;

        public bool Supported => _hw.ThresholdsSupported;
        public bool DischargeSupported => _hw.Behaviours.Contains("force-discharge");

        public ChargeStateMachine(IChargeHardware hardware, int dischargeFloorPct = 20)
        {
            _hw = hardware;
            Floor = dischargeFloorPct;
            Limit = ClampLimit(_hw.LoadLimit() ?? 100);
        }

        public static int ClampLimit(int v) => Math.Max(50, Math.Min(100, v));

        private void ApplyLimit()
        {
            if (!_hw.ThresholdsSupported) return;
            var end = Limit;
            var start = end >= 100 ? 95 : end - 5;
            _hw.WriteThresholds(start, end);
        }

        // ---- public API --------------------------------------------------

        /// <summary>Make hardware consistent with saved state; special modes never survive restart.</summary>
        public void ApplyStartup()
        {
            if (!_hw.ThresholdsSupported) return;
            try { _hw.WriteBehaviour("auto"); } catch (Exception) { }
            try { ApplyLimit(); } catch (Exception) { }
        }

        public void Shutdown()
        {
            if (Mode != ChargeMode.Limit)
            {
                try { _hw.WriteBehaviour("auto"); } catch (Exception) { }
                try { ApplyLimit(); } catch (Exception) { }
            }
        }

        public void SetLimit(int pct)
        {
            var prev = Limit;
            Limit = ClampLimit(pct);
            try
            {
                if (Mode == ChargeMode.Limit) ApplyLimit();
            }
            catch (Exception)
            {
                Limit = prev;  // keep state and hardware consistent
                throw;
            }
            if (Mode == ChargeMode.Discharge && Target < Limit) Target = Limit;
            _hw.SaveLimit(Limit);
        }

        public void StartTopUp()
        {
            Mode = ChargeMode.Topup;
            _hw.WriteBehaviour("auto");
            _hw.WriteThresholds(95, 100);
        }

        public void StartDischarge(int requested)
        {
            if (!DischargeSupported) throw new ChargeException(95, "force-discharge not supported");
            var t = requested > 0 ? requested : Limit;
            Target = Math.Max(Floor, Math.Min(99, t));
            Mode = ChargeMode.Discharge;
            _hw.WriteBehaviour("force-discharge");
        }

        public void Cancel()
        {
            Mode = ChargeMode.Limit;
            Target = 0;
            _hw.WriteBehaviour("auto");
            ApplyLimit();
        }

        /// <summary>Called every sample; ends special modes when their goal is reached. Returns true when a special mode was ended.</summary>
        public bool Tick(int batPct, string batStatus)
        {
            if (Mode == ChargeMode.Limit) return false;
            if (Mode == ChargeMode.Topup && (batStatus == "Full" || batPct >= 100))
            {
                try { Cancel(); } catch (Exception) { }
                return true;
            }
            if (Mode == ChargeMode.Discharge && batPct <= Target)
            {
                try { Cancel(); } catch (Exception) { }
                return true;
            }
            return false;
        }

        public Dictionary<string, object?> State => new Dictionary<string, object?>
        {
            ["charge_mode"] = Mode.Raw(), ["charge_limit"] = Limit, ["charge_target"] = Target,
        };
    }
}

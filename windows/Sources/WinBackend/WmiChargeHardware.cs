// A charge limit carried by a single writable WMI property, which is how several vendors expose it
// (HP, and any provider shaped like it). The provider publishes one value, not a start/stop pair,
// so the start threshold is the limit minus the usual hysteresis - the same 5 points the Linux
// sysfs and Lenovo paths use - and the hardware itself decides when to resume.
//
// Implemented against the published interface, but not verified on the vendor's hardware. See
// docs/charge-control.md: an unverified backend still only writes once its probe has confirmed the
// interface exists, which is what keeps it inert on the wrong machine.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using ClearPower.Core;

namespace ClearPower.Win
{
    public interface IWmiChargeLimit
    {
        /// <summary>Read the current limit, or null when it cannot be read.</summary>
        int? Read();
        /// <summary>Write a new limit in percent; throw when the provider rejects it.</summary>
        void Write(int percent);
        /// <summary>Human-readable description of the provider, for the snapshot.</summary>
        string Method { get; }
    }

    /// <summary>Where a vendor backend keeps its limit between runs.</summary>
    public interface ILimitStore
    {
        int? Load();
        void Save(int limit);
    }

    public sealed class WmiChargeHardware : IChargeHardware, IChargeHardwareInfo
    {
        private readonly IWmiChargeLimit _provider;
        private readonly Func<int?> _load;
        private readonly Action<int> _save;
        public const int Hysteresis = 5;

        /// <param name="store">Where the limit persists. Injectable so a test does not write to the
        /// real application state.</param>
        public WmiChargeHardware(IWmiChargeLimit provider, bool thresholdsSupported = true, ILimitStore? store = null)
        {
            _provider = provider;
            _load = store == null ? (Func<int?>)LimitStore.Load : store.Load;
            _save = store == null ? (Action<int>)LimitStore.Save : store.Save;
            ThresholdsSupported = thresholdsSupported;
        }

        public bool ThresholdsSupported { get; }
        public IReadOnlyList<string> Behaviours => new[] { "auto" };
        public string Method => _provider.Method;

        /// <summary>Providers publish one value, so both thresholds carry it.</summary>
        public void WriteThresholds(int start, int end)
        {
            if (!ThresholdsSupported)
                throw new ChargeException(95, $"{Method} is read-only on this machine");
            _provider.Write(end);
        }

        /// <summary>No provider here can force a discharge, so this is a no-op.</summary>
        public void WriteBehaviour(string behaviour) { }

        public int? LoadLimit() => _load();
        public void SaveLimit(int limit) => _save(limit);

        public Dictionary<string, object?> ExtraState() => new Dictionary<string, object?>
        {
            ["charge_start_threshold"] = (Read() ?? 100) - Hysteresis,
            ["charge_end_threshold"] = Read() ?? -1,
            ["charge_threshold_enabled"] = ThresholdsSupported,
            ["control_method"] = "vendor-wmi",
        };

        public void Reassert()
        {
            // Some firmware forgets the limit across a resume; re-apply the saved one.
            var saved = _load();
            if (saved is int v && ThresholdsSupported) _provider.Write(v);
        }

        private int? Read() { try { return _provider.Read(); } catch (Exception) { return null; } }
    }

    // ---- HP -----------------------------------------------------------------------------
    // HP laptops publish a battery charge limit through root\wmi. The provider has changed shape
    // between generations, so this tries each published spelling rather than assuming one:
    //
    //   HPBatteryChargeLimit              ChargeLimit / SetChargeLimit(percent)
    //   HP_BIOSSettingInterface           BIOS setting "Battery Health Manager"
    //
    // Only the straightforward property-and-setter form is driven automatically; a provider whose
    // method signature does not match is reported rather than called blindly.
    internal sealed class HpChargeLimit : IWmiChargeLimit
    {
        private const string Ns = @"root\wmi";
        private readonly string _className;
        private readonly string _property;
        private readonly string? _setter;

        public string Method => $"hp-wmi ({_className})";

        /// <summary>True when the provider exposes a setter that can be called.</summary>
        public bool Writable => _setter != null;

        private HpChargeLimit(string className, string property, string? setter)
        {
            _className = className;
            _property = property;
            _setter = setter;
        }

        /// <summary>Find a usable HP charge-limit provider, or null when this is not an HP.</summary>
        public static HpChargeLimit? Detect(Action<string> log)
        {
            // Preferred: a plain property plus a setter method.
            foreach (var (cls, prop, setter) in new[]
            {
                ("HPBatteryChargeLimit", "ChargeLimit", "SetChargeLimit"),
                ("HPBatteryChargeLimit", "ChargeLimit", null),
                ("HP_BIOSSettingInterface", "ChargeLimit", "SetChargeLimit"),
            })
            {
                if (!WmiProbe.ClassExists(Ns, cls)) continue;
                var members = WmiProbe.DescribeClass(Ns, cls);
                var hasProp = members.Any(m => !m.IsMethod && m.Name.Equals(prop, StringComparison.OrdinalIgnoreCase));
                if (!hasProp) continue;
                var hasSetter = setter != null && members.Any(m => m.IsMethod && m.Name.Equals(setter, StringComparison.OrdinalIgnoreCase));
                log($"charge control: HP provider {cls}.{prop}{(hasSetter ? " + " + setter : " (read-only)")}");
                return new HpChargeLimit(cls, prop, hasSetter ? setter : null);
            }
            return null;
        }

        public int? Read()
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(Ns), new ObjectQuery("SELECT * FROM " + _className));
            foreach (ManagementObject o in searcher.Get())
            {
                var v = o[_property];
                if (v != null) return Convert.ToInt32(v);
            }
            return null;
        }

        public void Write(int percent)
        {
            if (_setter == null)
                throw new ChargeException(95, $"{_className} exposes no charge-limit setter on this machine");
            using var searcher = new ManagementObjectSearcher(new ManagementScope(Ns), new ObjectQuery("SELECT * FROM " + _className));
            foreach (ManagementObject o in searcher.Get())
            {
                o.InvokeMethod(_setter, new object[] { (uint)percent });
                return;
            }
            throw new ChargeException(95, $"{_className} has no instance to write");
        }
    }
}

// Battery readings through the battery class driver (IOCTL_BATTERY_*), the same interface
// Windows itself uses; no privileges required. Port of daemon/clearpowerd/sources/battery.py
// and sources/usbpd.py (AC presence from GetSystemPowerStatus; Windows exposes no negotiated
// USB-PD wattage, so adapter_max_w is 0).
//
// Sign convention: bat_w positive = into the battery (charging), negative = out.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ClearPower.Core;
using Microsoft.Win32.SafeHandles;
using static ClearPower.Win.NativeMethods;

namespace ClearPower.Win
{
    public sealed class BatterySource : IDisposable
    {
        private SafeFileHandle? _dev;
        private uint _tag;
        private double _nextOpen;
        private readonly double _slowEvery;
        private double _slowAt = -1e9;
        private Dictionary<string, object?> _slow = new Dictionary<string, object?>();
        private bool _relative;
        public bool Present => _dev != null && !_dev.IsInvalid;
        public Action<string> Log { get; set; } = _ => { };

        public BatterySource(double slowEveryS = 30)
        {
            _slowEvery = slowEveryS;
            Open();
        }

        // ---- device discovery ----------------------------------------------------
        private void Open()
        {
            _nextOpen = Clock.MonotonicNow() + 10;
            var guid = GUID_DEVCLASS_BATTERY;
            var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return;
            try
            {
                for (uint i = 0; i < 4; i++)
                {
                    var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did)) break;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out var need, IntPtr.Zero);
                    if (need == 0) continue;
                    var buf = Marshal.AllocHGlobal((int)need);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);  // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, buf, need, out need, IntPtr.Zero)) continue;
                        var path = Marshal.PtrToStringUni(buf + 4) ?? "";
                        var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                        if (h.IsInvalid)
                            h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                        if (h.IsInvalid) continue;
                        // A tag of 0 means "no battery in this bay"; try the next interface.
                        if (!QueryTag(h, out var tag) || tag == 0) { h.Dispose(); continue; }
                        _dev = h;
                        _tag = tag;
                        Log($"battery: {path} (tag {tag})");
                        return;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buf);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static bool QueryTag(SafeFileHandle h, out uint tag)
        {
            var inb = BitConverter.GetBytes(0u);   // wait 0 ms
            var outb = new byte[4];
            var ok = DeviceIoControl(h, IOCTL_BATTERY_QUERY_TAG, inb, 4, outb, 4, out _, IntPtr.Zero);
            tag = ok ? BitConverter.ToUInt32(outb, 0) : 0;
            return ok;
        }

        private byte[]? QueryInformation(int level, int outSize)
        {
            if (_dev == null) return null;
            var inb = new byte[12];
            BitConverter.GetBytes(_tag).CopyTo(inb, 0);
            BitConverter.GetBytes(level).CopyTo(inb, 4);
            var outb = new byte[outSize];
            if (!DeviceIoControl(_dev, IOCTL_BATTERY_QUERY_INFORMATION, inb, 12, outb, (uint)outSize, out var ret, IntPtr.Zero))
                return null;
            Array.Resize(ref outb, (int)ret);
            return outb;
        }

        private string QueryString(int level)
        {
            var b = QueryInformation(level, 512);
            if (b == null || b.Length < 2) return "";
            return Encoding.Unicode.GetString(b).TrimEnd('\0').Trim();
        }

        // ---- reads ---------------------------------------------------------------------
        public void Invalidate() => _slowAt = -1e9;

        private Dictionary<string, object?> SlowAttrs()
        {
            var now = Clock.MonotonicNow();
            if (now - _slowAt < _slowEvery) return _slow;
            var d = new Dictionary<string, object?>();
            var info = QueryInformation(0 /* BatteryInformation */, 40);
            if (info != null && info.Length >= 40)
            {
                var caps = BitConverter.ToUInt32(info, 0);
                _relative = (caps & BATTERY_CAPACITY_RELATIVE) != 0;
                var design = BitConverter.ToUInt32(info, 12);
                var full = BitConverter.ToUInt32(info, 16);
                d["bat_design_wh"] = (!_relative && design != BATTERY_UNKNOWN_CAPACITY) ? design / 1000.0 : 0.0;
                d["bat_full_wh"] = (!_relative && full != BATTERY_UNKNOWN_CAPACITY) ? full / 1000.0 : 0.0;
                d["cycle_count"] = (int)BitConverter.ToUInt32(info, 36);
                d["bat_chemistry"] = Encoding.ASCII.GetString(info, 8, 4).TrimEnd('\0');
            }
            d["bat_model"] = QueryString(4 /* BatteryDeviceName */);
            d["bat_manufacturer"] = QueryString(6 /* BatteryManufactureName */);
            // Windows has no charge_behaviour / threshold attributes at this level; the vendor
            // interface (Lenovo Power Manager) reports them through the charge state instead.
            d["charge_behaviour"] = "auto";
            _slow = d;
            _slowAt = now;
            return d;
        }

        public Dictionary<string, object?> Read()
        {
            var outp = new Dictionary<string, object?>();
            GetSystemPowerStatus(out var sps);
            var onAc = sps.ACLineStatus == 1;
            outp["on_ac"] = onAc;
            outp["adapter_max_w"] = 0.0;
            outp["adapter_v"] = 0.0;

            if (_dev == null)
            {
                if (Clock.MonotonicNow() >= _nextOpen) Open();
                if (_dev == null)
                {
                    outp["bat_present"] = false;
                    return outp;
                }
            }
            // BATTERY_WAIT_STATUS { Tag, Timeout, PowerState, LowCapacity, HighCapacity }
            var inb = new byte[20];
            BitConverter.GetBytes(_tag).CopyTo(inb, 0);
            var st = new byte[16];
            if (!DeviceIoControl(_dev, IOCTL_BATTERY_QUERY_STATUS, inb, 20, st, 16, out _, IntPtr.Zero))
            {
                // Tag changes when the battery is removed/reinserted: rediscover.
                Log("battery status query failed; reopening");
                _dev.Dispose(); _dev = null;
                outp["bat_present"] = false;
                return outp;
            }
            var power = BitConverter.ToUInt32(st, 0);
            var capacity = BitConverter.ToUInt32(st, 4);
            var voltage = BitConverter.ToUInt32(st, 8);
            var rate = BitConverter.ToInt32(st, 12);
            if (rate == BATTERY_UNKNOWN_RATE) rate = 0;

            var slow = SlowAttrs();
            var fullWh = slow.D("bat_full_wh", 0);
            var energyWh = (!_relative && capacity != BATTERY_UNKNOWN_CAPACITY) ? capacity / 1000.0 : 0.0;
            var charging = (power & BATTERY_CHARGING) != 0;
            var discharging = (power & BATTERY_DISCHARGING) != 0;
            int pct = fullWh > 0 ? (int)Math.Round(100.0 * energyWh / fullWh) : sps.BatteryLifePercent;
            if (pct < 0 || pct > 100) pct = Math.Max(0, Math.Min(100, (int)sps.BatteryLifePercent));

            string status;
            double batW;
            if (charging && rate > 0)
            {
                status = "Charging"; batW = rate / 1000.0;
            }
            else if (discharging && rate < 0)
            {
                status = "Discharging"; batW = rate / 1000.0;
            }
            else if (!onAc && rate != 0)
            {
                status = "Discharging"; batW = -Math.Abs(rate) / 1000.0;
            }
            else
            {
                status = (pct >= 100 || (fullWh > 0 && energyWh >= fullWh)) ? "Full" : (onAc ? "Not charging" : "Discharging");
                batW = 0.0;
            }

            outp["bat_present"] = true;
            outp["bat_status"] = status;
            outp["bat_pct"] = pct;
            outp["bat_w"] = batW;
            outp["bat_energy_wh"] = energyWh;
            outp["bat_v"] = voltage / 1000.0;
            outp.MergeFrom(slow);
            return outp;
        }

        public void Dispose()
        {
            _dev?.Dispose();
            _dev = null;
        }
    }
}

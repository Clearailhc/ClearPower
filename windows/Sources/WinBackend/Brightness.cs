// Panel brightness through WMI (root\wmi WmiMonitorBrightness / WmiMonitorBrightnessMethods),
// 0..100, and the console display state from the power-setting notification.
// Port of daemon/clearpowerd/sources/backlight.py; also the IBrightnessControl used by
// display calibration.
using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using ClearPower.Core;
using static ClearPower.Win.NativeMethods;

namespace ClearPower.Win
{
    public sealed class Brightness : IBrightnessControl
    {
        private static readonly ManagementScope Scope = new ManagementScope(@"root\wmi");
        private bool _displayOn = true;
        private DeviceNotifyCallbackRoutine? _cb;   // keep the delegate alive
        private IntPtr _reg;
        public Action<string> Log { get; set; } = _ => { };

        public bool Available { get; private set; }
        public int Max => 100;

        public Brightness()
        {
            Available = ReadRaw() != null;
            RegisterDisplayState();
        }

        private void RegisterDisplayState()
        {
            try
            {
                _cb = (ctx, type, setting) =>
                {
                    if (type == PBT_POWERSETTINGCHANGE && setting != IntPtr.Zero)
                    {
                        var s = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(setting);
                        if (s.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
                        {
                            _displayOn = s.Data != 0;  // 0 off, 1 on, 2 dimmed
                            Log($"display state: {s.Data}");
                        }
                    }
                    return 0;
                };
                var p = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { Callback = _cb, Context = IntPtr.Zero };
                var guid = GUID_CONSOLE_DISPLAY_STATE;
                PowerSettingRegisterNotification(ref guid, DEVICE_NOTIFY_CALLBACK, ref p, out _reg);
            }
            catch (Exception e)
            {
                Log($"display state notification unavailable: {e.Message}");
            }
        }

        public int? ReadRaw()
        {
            try
            {
                using var s = new ManagementObjectSearcher(Scope, new ObjectQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness"));
                foreach (ManagementObject o in s.Get())
                    return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch (Exception)
            {
            }
            return null;
        }

        public void SetRaw(int value)
        {
            value = Math.Max(0, Math.Min(100, value));
            using var s = new ManagementObjectSearcher(Scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
            foreach (ManagementObject o in s.Get())
            {
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)value });
                return;
            }
            throw new InvalidOperationException("no WMI brightness device");
        }

        public Dictionary<string, object?> Read()
        {
            var b = ReadRaw();
            if (b == null)
                return new Dictionary<string, object?> { ["brightness_pct"] = -1.0, ["brightness_raw"] = -1, ["display_on"] = true };
            return new Dictionary<string, object?>
            {
                ["brightness_pct"] = (double)b.Value,
                ["brightness_raw"] = b.Value,
                ["display_on"] = _displayOn && b.Value > 0,
            };
        }
    }
}

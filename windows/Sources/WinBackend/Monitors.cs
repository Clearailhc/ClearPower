// Which monitor a point or window is on, and that monitor's usable area in WPF layout units.
//
// SystemParameters.WorkArea is *always the primary monitor* in device-independent pixels, which
// is the root of the popover drifting and mis-sizing: on a 225 % primary display the work area
// reads 1280x752 (raw pixels) while a DIP is only 0.444 px, so the real usable box is 568x334 DIP
// - shorter than the popover itself, which made every clamp a no-op. The same value is wrong on
// any secondary display whatever its scale.
//
// Everything here is physical pixels in and layout units out, at the target monitor's scale, so
// callers never have to mix the two.
using System;
using System.Runtime.InteropServices;
using ClearPower.Core;

namespace ClearPower.Win
{
    public struct WorkArea
    {
        public LayoutRect Dip;
        public double Scale;
        /// <summary>True when Windows refused to answer, so the caller should fall back.</summary>
        public bool Fallback;
    }

    public static class Monitors
    {
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        /// <summary>Usable area (taskbar excluded) in layout units, for the monitor holding a point.</summary>
        public static WorkArea ForPoint(double physicalX, double physicalY) =>
            Resolve(MonitorFromPoint(new POINT { X = (int)Math.Round(physicalX), Y = (int)Math.Round(physicalY) }, MONITOR_DEFAULTTONEAREST));

        /// <summary>Usable area in layout units, for the monitor a window centre is on (nearest if it is gone).</summary>
        public static WorkArea ForWindow(IntPtr hwnd) =>
            Resolve(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

        private static WorkArea Resolve(IntPtr monitor)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return new WorkArea { Fallback = true };

            // GetDpiForMonitor is the documented way to ask about a monitor other than the window's;
            // GetDpiForWindow would answer for wherever the window currently happens to be.
            var scale = 1.0;
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;
            else return new WorkArea { Fallback = true };

            return new WorkArea
            {
                Scale = scale,
                Dip = new LayoutRect(
                    info.rcWork.Left / scale,
                    info.rcWork.Top / scale,
                    (info.rcWork.Right - info.rcWork.Left) / scale,
                    (info.rcWork.Bottom - info.rcWork.Top) / scale),
            };
        }
    }
}

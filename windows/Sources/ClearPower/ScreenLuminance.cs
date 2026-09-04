// Average linear luminance of the primary screen (0..1) from a 48x30 GDI downscale
// (StretchBlt HALFTONE): a few milliseconds, and only a single mean leaves this class.
// Port of extension/clearpower@lhc/content.js.
using System;
using System.Runtime.InteropServices;

namespace ClearPower.App
{
    public static class ScreenLuminance
    {
        private const int W = 48, H = 30;

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
        [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, int sw, int sh, uint rop);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public uint color0;
        }

        private static readonly double[] LinearLut = BuildLut();

        private static double[] BuildLut()
        {
            var lut = new double[256];
            for (int i = 0; i < 256; i++)
            {
                var v = i / 255.0;
                lut[i] = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return lut;
        }

        /// <summary>Mean linear luminance of the primary display, or -1 when it cannot be captured.</summary>
        public static double Sample()
        {
            var sw = GetSystemMetrics(0); var sh = GetSystemMetrics(1);
            if (sw <= 0 || sh <= 0) return -1;
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return -1;
            var mem = IntPtr.Zero; var bmp = IntPtr.Zero;
            try
            {
                mem = CreateCompatibleDC(screen);
                var bmi = new BITMAPINFO { biSize = 40, biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
                bmp = CreateDIBSection(mem, ref bmi, 0, out var bits, IntPtr.Zero, 0);
                if (bmp == IntPtr.Zero) return -1;
                var old = SelectObject(mem, bmp);
                SetStretchBltMode(mem, 4 /* HALFTONE */);
                SetBrushOrgEx(mem, 0, 0, IntPtr.Zero);
                if (!StretchBlt(mem, 0, 0, W, H, screen, 0, 0, sw, sh, 0x00CC0020 /* SRCCOPY */)) return -1;
                var px = new byte[W * H * 4];
                Marshal.Copy(bits, px, 0, px.Length);
                SelectObject(mem, old);
                double sum = 0;
                for (int i = 0; i < px.Length; i += 4)
                    sum += 0.2126 * LinearLut[px[i + 2]] + 0.7152 * LinearLut[px[i + 1]] + 0.0722 * LinearLut[px[i]];
                return sum / (W * H);
            }
            catch (Exception)
            {
                return -1;
            }
            finally
            {
                if (bmp != IntPtr.Zero) DeleteObject(bmp);
                if (mem != IntPtr.Zero) DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}

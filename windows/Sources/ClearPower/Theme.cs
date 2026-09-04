// Light/dark palette following the Windows app theme (AppsUseLightTheme) and the tray
// text colour following the system theme (SystemUsesLightTheme).
using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ClearPower.App
{
    public static class Theme
    {
        public static bool AppsLight { get; private set; }
        public static bool SystemLight { get; private set; }
        public static event Action? Changed;

        public static Color Bg => AppsLight ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20);
        public static Color Fg => AppsLight ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Color.FromRgb(0xF0, 0xF0, 0xF0);
        public static Color Dim => AppsLight ? Color.FromArgb(0xE6, 0x60, 0x60, 0x60) : Color.FromArgb(0xE6, 0xA0, 0xA0, 0xA0);
        public static Color Pill => Color.FromArgb(0x29, 0x80, 0x80, 0x80);
        public static Color PillHover => Color.FromArgb(0x47, 0x80, 0x80, 0x80);
        public static Color Checked => Color.FromArgb(0x66, 0x5C, 0x99, 0xEB);
        public static Color Box => Color.FromArgb(0x1F, 0x80, 0x80, 0x80);
        public static Color Border => AppsLight ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
        public static Color Warn => Color.FromRgb(0xE0, 0x7A, 0x5F);
        public static Color Accent => Color.FromRgb(0x5C, 0x99, 0xEB);

        public static void Refresh()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                AppsLight = Convert.ToInt32(k?.GetValue("AppsUseLightTheme", 1) ?? 1) != 0;
                SystemLight = Convert.ToInt32(k?.GetValue("SystemUsesLightTheme", 1) ?? 1) != 0;
            }
            catch (Exception)
            {
                AppsLight = true; SystemLight = true;
            }
            Apply();
            Changed?.Invoke();
        }

        private static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>Publishes the palette as application resources (used by XAML via DynamicResource).</summary>
        private static void Apply()
        {
            var r = Application.Current?.Resources;
            if (r == null) return;
            r["BgBrush"] = Brush(Bg);
            r["FgBrush"] = Brush(Fg);
            r["DimBrush"] = Brush(Dim);
            r["PillBrush"] = Brush(Pill);
            r["PillHoverBrush"] = Brush(PillHover);
            r["CheckedBrush"] = Brush(Checked);
            r["BoxBrush"] = Brush(Box);
            r["BorderBrush"] = Brush(Border);
            r["WarnBrush"] = Brush(Warn);
            r["AccentBrush"] = Brush(Accent);
        }
    }
}

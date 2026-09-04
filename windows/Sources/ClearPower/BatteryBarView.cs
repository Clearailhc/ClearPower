// Battery level bar with the charge-limit marker. Port of extension/clearpower@lhc/batteryBar.js.
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClearPower.App
{
    public sealed class BatteryBarView : FrameworkElement
    {
        public int Pct { get; private set; }
        public int Limit { get; private set; } = 100;
        public string Status { get; private set; } = "";
        public string Mode { get; private set; } = "limit";
        public bool OnAc { get; private set; }

        private static readonly Color Charging = SankeyPalette.Rgb(0.40, 0.78, 0.52);
        private static readonly Color Inhibited = SankeyPalette.Rgb(0.38, 0.70, 0.94);
        private static readonly Color Discharging = SankeyPalette.Rgb(0.62, 0.65, 0.69);
        private static readonly Color Forced = SankeyPalette.Rgb(0.95, 0.62, 0.32);

        public BatteryBarView()
        {
            Height = 28;
        }

        public void Update(int? pct = null, int? limit = null, string? status = null, string? mode = null, bool? onAc = null)
        {
            if (pct != null) Pct = pct.Value;
            if (limit != null) Limit = limit.Value;
            if (status != null) Status = status;
            if (mode != null) Mode = mode;
            if (onAc != null) OnAc = onAc.Value;
            InvalidateVisual();
        }

        private static Color Mul(Color c, double k) => Color.FromRgb((byte)Math.Min(255, c.R * k), (byte)Math.Min(255, c.G * k), (byte)Math.Min(255, c.B * k));

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth; var h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            var r = h / 2;
            var track = new SolidColorBrush(Color.FromArgb(0x2E, 0x80, 0x80, 0x80)); track.Freeze();
            dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), r, r);

            var kind = Discharging; var glyph = "–";
            if (Mode == "discharge") { kind = Forced; glyph = "⤓"; }
            else if (Status == "Charging") { kind = Charging; glyph = "⚡"; }
            else if (OnAc) { kind = Inhibited; glyph = "⏸"; }
            var fillW = Math.Max(h, w * Math.Min(Pct, 100) / 100.0);
            var g = new LinearGradientBrush(
                Color.FromArgb(0xEB, Mul(kind, 1.08).R, Mul(kind, 1.08).G, Mul(kind, 1.08).B),
                Color.FromArgb(0xEB, Mul(kind, 0.88).R, Mul(kind, 0.88).G, Mul(kind, 0.88).B), 90);
            g.Freeze();
            dc.DrawRoundedRectangle(g, null, new Rect(0, 0, fillW, h), r, r);

            var fg = Theme.Fg;
            if (Limit < 100)
            {
                var x = Math.Round(w * Limit / 100.0) + 0.5;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xB3, fg.R, fg.G, fg.B)), 1.5) { DashStyle = new DashStyle(new[] { 1.5, 2.0 }, 0) };
                pen.Freeze();
                dc.DrawLine(pen, new Point(x, 5), new Point(x, h - 5));
            }

            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            void Paint(string text, double x, double tw)
            {
                var onFill = x + tw <= fillW - 6;
                var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold, 13, new SolidColorBrush(onFill ? Color.FromArgb(0xF2, 255, 255, 255) : fg), dpi);
                var y = (h - ft.Height) / 2;
                if (onFill)
                {
                    var shadow = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold, 13, new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), dpi);
                    dc.DrawText(shadow, new Point(x + 1, y + 1));
                }
                dc.DrawText(ft, new Point(x, y));
            }
            var pctText = new FormattedText($"{Pct}%", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold, 13, Brushes.Black, dpi);
            Paint($"{Pct}%", 12, pctText.Width);
            var gl = new FormattedText(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold, 13, Brushes.Black, dpi);
            Paint(glyph, (w - gl.Width) / 2, gl.Width);
        }
    }
}

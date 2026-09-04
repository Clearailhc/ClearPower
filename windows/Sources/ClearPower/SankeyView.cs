// The power-flow diagram: sources → system → sinks. Port of the drawing half of
// extension/clearpower@lhc/sankey.js onto a WPF FrameworkElement.
//
// Seam-free bands: (1) clip away the node cards so bands slide under their rounded
// outlines, (2) paint every band opaque into one opacity group with a hair of overlap,
// (3) composite the group once at 32 % — no antialiased edges between neighbours.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClearPower.Core;

namespace ClearPower.App
{
    public sealed class SankeyView : FrameworkElement
    {
        public SankeyModel Model { get; } = new SankeyModel();
        private readonly DispatcherTimer _timer;
        private DateTime _lastTick;
        private bool _active;
        private SankeyGraph? _lastGraph;
        private string? _hover;
        private readonly ToolTip _tip = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse, StaysOpen = false };
        private Typeface _typeface = new Typeface("Segoe UI");
        private Typeface _bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        public double FontSizeBase { get; set; } = 13;

        public SankeyView()
        {
            Height = Model.Height;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000 / SankeyModel.Fps) };
            _timer.Tick += (_, _) => Frame();
            Model.ReduceMotion = () => !SystemParameters.ClientAreaAnimation;
            ToolTipService.SetInitialShowDelay(this, 250);
            ToolTipService.SetShowDuration(this, 20000);
            MouseMove += OnMouseMove;
            MouseLeave += (_, _) => SetHover(null);
            Unloaded += (_, _) => _timer.Stop();
        }

        public string FlowMode
        {
            get => Model.FlowMode;
            set { Model.FlowMode = value; if (_active) StartTimer(); InvalidateVisual(); }
        }

        /// <summary>New data from the engine. Eases in while visible, snaps otherwise.</summary>
        public void Update(Dictionary<string, object?> snap)
        {
            Model.Update(snap, _active);
            if (Math.Abs(Height - Model.Height) > 0.5) Height = Model.Height;
            if (_active) StartTimer(); else InvalidateVisual();
        }

        /// <summary>Language change etc.: redraw.</summary>
        public void Invalidate() => InvalidateVisual();

        /// <summary>Only animate while the popover is open: zero cost when closed.</summary>
        public void SetActive(bool active)
        {
            _active = active;
            if (active) StartTimer(); else _timer.Stop();
        }

        private void StartTimer()
        {
            if (_timer.IsEnabled) return;
            _lastTick = DateTime.UtcNow;
            _timer.Start();
        }

        private void Frame()
        {
            var now = DateTime.UtcNow;
            var dt = Math.Min((now - _lastTick).TotalSeconds, 0.25);
            _lastTick = now;
            var keep = Model.Frame(dt);
            InvalidateVisual();
            if (!keep) _timer.Stop();  // idle until the next sample
        }

        // ---- hover tooltip: name · watts + what the node contains ----
        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var g = _lastGraph;
            if (g == null) return;
            var p = e.GetPosition(this);
            string? hit = null;
            foreach (var n in g.Ordered)
                if (p.X >= n.X && p.X <= n.X + n.WPx && p.Y >= n.Y && p.Y <= n.Y + n.H) { hit = n.Id; break; }
            SetHover(hit);
        }

        private void SetHover(string? id)
        {
            if (id == _hover) return;
            _hover = id;
            InvalidateVisual();
            if (id == null || _lastGraph == null || !_lastGraph.Nodes.TryGetValue(id, out var n))
            {
                _tip.IsOpen = false;
                ToolTip = null;
                return;
            }
            var text = $"{n.Label} · {(n.Approx ? "≈" : "")}{I18n.FmtW(n.W)}\n{I18n.T(SankeyModel.TipKey(n))}";
            _tip.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 };
            ToolTip = _tip;
        }

        // ---- drawing ----
        private static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);

        private static StreamGeometry Band(double x0, double y0, double x1, double y1, double t)
        {
            var mx = (x0 + x1) / 2;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x0, y0), true, true);
                c.BezierTo(new Point(mx, y0), new Point(mx, y1), new Point(x1, y1), true, false);
                c.LineTo(new Point(x1, y1 + t), true, false);
                c.BezierTo(new Point(mx, y1 + t), new Point(mx, y0 + t), new Point(x0, y0 + t), true, false);
            }
            g.Freeze();
            return g;
        }

        private FormattedText Text(string s, Typeface tf, double size, Color color, double maxWidth)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, size, new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1,
            };
            if (maxWidth > 0) ft.MaxTextWidth = maxWidth;
            return ft;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var W = ActualWidth; var H = ActualHeight;
            var s = Model.Shown;
            // Transparent hit-test surface so MouseMove works over empty areas.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, W, H));
            if (s == null || W < 100) return;
            var g = Model.Model(s);
            Model.Layout(g, W, H);
            _lastGraph = g;
            var scale = g.Scale;
            var fg = Theme.Fg;

            // ---- bands ----
            var sheen = _active && Model.SheenEnabled();
            const double R = 12, EXT = 10, OVERLAP = 0.35;
            var clip = new PathGeometry { FillRule = FillRule.EvenOdd };
            clip.AddGeometry(new RectangleGeometry(new Rect(0, 0, W, H)));
            foreach (var n in g.Ordered)
                clip.AddGeometry(new RectangleGeometry(new Rect(n.X, n.Y, n.WPx, n.H), R, R));
            clip.Freeze();
            dc.PushClip(clip);
            dc.PushOpacity(0.32);
            foreach (var f in g.Flows)
            {
                var a = g.Nodes[f.A]; var b = g.Nodes[f.B];
                var t = Math.Max(f.W * scale, 2);
                var x0 = a.X + a.WPx - EXT; var y0 = a.Y + a.OutOff - OVERLAP;
                var x1 = b.X + EXT; var y1 = b.Y + b.InOff - OVERLAP;
                a.OutOff += t;
                b.InOff += t;
                var band = Band(x0, y0, x1, y1, t + 2 * OVERLAP);
                var grad = new LinearGradientBrush(a.Color, b.Color, new Point(x0, 0), new Point(x1, 0)) { MappingMode = BrushMappingMode.Absolute };
                grad.Freeze();
                dc.DrawGeometry(grad, null, band);
                if (sheen)
                {
                    var pos = -0.3 + Model.Phase * 1.6;
                    const double half = 0.3;
                    double Tri(double x) => Math.Max(0, 1 - Math.Abs(x - pos) / half);
                    var stops = new[] { 0, pos - half, pos, pos + half, 1 }.Select(x => Math.Min(1, Math.Max(0, x))).OrderBy(x => x).ToList();
                    var sg = new LinearGradientBrush { StartPoint = new Point(x0, 0), EndPoint = new Point(x1, 0), MappingMode = BrushMappingMode.Absolute };
                    foreach (var x in stops) sg.GradientStops.Add(new GradientStop(WithAlpha(Colors.White, 0.5 * Tri(x)), x));
                    sg.Freeze();
                    dc.DrawGeometry(sg, null, band);
                }
            }
            dc.Pop();
            dc.Pop();

            // ---- nodes: translucent card (brighter under the pointer), glyph, small name, bold watts.
            // The name, then the glyph, are dropped when the card is too short.
            const double GLYPH_H = 18;
            foreach (var n in g.Ordered)
            {
                var card = new SolidColorBrush(WithAlpha(n.Color, _hover == n.Id ? 0.30 : 0.18)); card.Freeze();
                var outline = new Pen(new SolidColorBrush(WithAlpha(n.Color, 0.55)), 1); outline.Freeze();
                dc.DrawRoundedRectangle(card, outline, new Rect(n.X, n.Y, n.WPx, n.H), R, R);
                var watts = Text((n.Approx ? "≈" : "") + I18n.FmtW(n.W), _bold, FontSizeBase, fg, n.WPx - 6);
                var name = Text(n.Label, _typeface, Math.Round(FontSizeBase * 0.78), WithAlpha(fg, 0.75), n.WPx - 6);
                var bw = watts.Width; var bh = watts.Height; var nw = name.Width; var nh = name.Height;
                var glyphId = n.Id == "batchg" ? "battery" : n.Id;
                var cx = n.X + n.WPx / 2;
                if (n.H >= GLYPH_H + nh + bh + 6)
                {
                    var top = n.Y + (n.H - GLYPH_H - nh - bh - 1) / 2;
                    Glyphs.Draw(dc, glyphId, cx, top + GLYPH_H / 2, n.Color);
                    dc.DrawText(name, new Point(cx - nw / 2, top + GLYPH_H));
                    dc.DrawText(watts, new Point(cx - bw / 2, top + GLYPH_H + nh - 1));
                }
                else if (n.H >= GLYPH_H + bh + 6)
                {
                    var top = n.Y + (n.H - GLYPH_H - bh - 2) / 2;
                    Glyphs.Draw(dc, glyphId, cx, top + GLYPH_H / 2, n.Color);
                    dc.DrawText(watts, new Point(cx - bw / 2, top + GLYPH_H + 2));
                }
                else
                {
                    dc.DrawText(watts, new Point(cx - bw / 2, n.Y + (n.H - bh) / 2));
                }
            }
        }
    }
}

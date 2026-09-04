// Tiny vector glyphs for Sankey nodes, drawn with the DrawingContext so they follow the
// node colour and need no icon theme. Port of extension/clearpower@lhc/glyphs.js:
// all are designed on a 16 px box centred at (cx, cy).
using System;
using System.Windows;
using System.Windows.Media;

namespace ClearPower.App
{
    public static class Glyphs
    {
        private static Pen StrokePen(Color c)
        {
            var p = new Pen(new SolidColorBrush(c), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            p.Freeze();
            return p;
        }

        private static void Line(DrawingContext dc, Pen pen, double x0, double y0, double x1, double y1)
            => dc.DrawLine(pen, new Point(x0, y0), new Point(x1, y1));

        private static void RoundRect(DrawingContext dc, Brush? fill, Pen? pen, double x, double y, double w, double h, double r)
        {
            r = Math.Min(r, Math.Min(h / 2, w / 2));
            dc.DrawRoundedRectangle(fill, pen, new Rect(x, y, w, h), r, r);
        }

        private static void Rect(DrawingContext dc, Brush fill, double x, double y, double w, double h)
            => dc.DrawRectangle(fill, null, new Rect(x, y, w, h));

        /// <summary>Draw glyph `id` (falls back to `other`) in colour `c`.</summary>
        public static void Draw(DrawingContext dc, string id, double cx, double cy, Color c)
        {
            var pen = StrokePen(c);
            var fill = new SolidColorBrush(c);
            fill.Freeze();
            switch (id)
            {
                case "adapter":              // wall plug: two prongs, body, cord
                    Line(dc, pen, cx - 3, cy - 7, cx - 3, cy - 3);
                    Line(dc, pen, cx + 3, cy - 7, cx + 3, cy - 3);
                    RoundRect(dc, null, pen, cx - 6, cy - 3, 12, 7, 2.5);
                    Line(dc, pen, cx, cy + 4, cx, cy + 7.5);
                    break;
                case "battery":
                case "batchg":
                    RoundRect(dc, null, pen, cx - 7.5, cy - 4, 13, 8, 1.5);
                    Rect(dc, fill, cx + 6, cy - 2, 2, 4);
                    RoundRect(dc, fill, null, cx - 5.5, cy - 2, 6.5, 4, 0.8);
                    break;
                case "pc":                   // laptop
                    RoundRect(dc, null, pen, cx - 6, cy - 5.5, 12, 8, 1.2);
                    Line(dc, pen, cx - 8, cy + 5, cx + 8, cy + 5);
                    break;
                case "cpu":                  // chip with pins
                    RoundRect(dc, null, pen, cx - 5, cy - 5, 10, 10, 1.8);
                    Rect(dc, fill, cx - 2.2, cy - 2.2, 4.4, 4.4);
                    foreach (var d in new[] { -3.0, 0, 3 })
                    {
                        Line(dc, pen, cx + d, cy - 7.5, cx + d, cy - 5);
                        Line(dc, pen, cx + d, cy + 5, cx + d, cy + 7.5);
                        Line(dc, pen, cx - 7.5, cy + d, cx - 5, cy + d);
                        Line(dc, pen, cx + 5, cy + d, cx + 7.5, cy + d);
                    }
                    break;
                case "soc":                  // system-on-chip: package with four tiles
                    RoundRect(dc, null, pen, cx - 6.5, cy - 6.5, 13, 13, 2.2);
                    foreach (var (dx, dy) in new[] { (-4.0, -4.0), (1, -4), (-4, 1), (1, 1) })
                        Rect(dc, fill, cx + dx, cy + dy, 3, 3);
                    break;
                case "gpu":                  // chip with a triangle (graphics)
                    {
                        RoundRect(dc, null, pen, cx - 6.5, cy - 6.5, 13, 13, 2.2);
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(new Point(cx, cy - 3.6), true, true);
                            ctx.LineTo(new Point(cx + 3.8, cy + 3), false, false);
                            ctx.LineTo(new Point(cx - 3.8, cy + 3), false, false);
                        }
                        g.Freeze();
                        dc.DrawGeometry(fill, null, g);
                        foreach (var d in new[] { -3.0, 3 })
                        {
                            Line(dc, pen, cx + d, cy - 8, cx + d, cy - 6.5);
                            Line(dc, pen, cx + d, cy + 6.5, cx + d, cy + 8);
                            Line(dc, pen, cx - 8, cy + d, cx - 6.5, cy + d);
                            Line(dc, pen, cx + 6.5, cy + d, cx + 8, cy + d);
                        }
                    }
                    break;
                case "mem":                  // RAM module: four chips on a stick with a notch
                    RoundRect(dc, null, pen, cx - 8, cy - 4, 16, 8, 1);
                    foreach (var d in new[] { -6.5, -2.5, 1.5, 5.5 })
                        Rect(dc, fill, cx + d, cy - 2, 2.2, 3);
                    Line(dc, pen, cx - 8, cy + 4, cx - 1, cy + 4);
                    Line(dc, pen, cx + 1, cy + 4, cx + 8, cy + 4);
                    Line(dc, pen, cx - 1, cy + 4, cx - 1, cy + 5.5);
                    Line(dc, pen, cx + 1, cy + 4, cx + 1, cy + 5.5);
                    break;
                case "disp":                 // monitor
                    RoundRect(dc, null, pen, cx - 7.5, cy - 6, 15, 10, 1.5);
                    Line(dc, pen, cx, cy + 4, cx, cy + 6.5);
                    Line(dc, pen, cx - 4, cy + 6.5, cx + 4, cy + 6.5);
                    break;
                default:                     // ellipsis
                    foreach (var d in new[] { -5.0, 0, 5 })
                        dc.DrawEllipse(fill, null, new Point(cx + d, cy), 1.6, 1.6);
                    break;
            }
        }
    }
}

// Window/screen geometry in logical points, never backing pixels. Port of the macOS
// WindowGeometry.swift plus the popover anchoring, so the arithmetic can be unit tested without
// a display. Every rect here is already in the coordinates WPF lays out in, i.e. divided by the
// target monitor's DPI scale - resolving that scale is platform code.
using System;

namespace ClearPower.Core
{
    public struct LayoutRect
    {
        public double X, Y, W, H;

        public LayoutRect(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }

        public double Right => X + W;
        public double Bottom => Y + H;
        public bool Contains(LayoutRect other) => other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
    }

    public static class WindowGeometry
    {
        /// <summary>
        /// Placement for an anchor given in *physical screen pixels* - a tray icon rectangle from
        /// Shell_NotifyIconGetRect, or a cursor position.
        ///
        /// The anchor must be converted with the target monitor's scale, not the window's current
        /// one: mixing the two is what made the popover drift across displays, and using the
        /// primary monitor's work area is what stopped it clamping at all.
        /// </summary>
        public static LayoutRect PlacementForPhysicalAnchor(
            LayoutRect anchorPx, double width, double height, LayoutRect workDip, double scale,
            double gap = 8, double margin = 4)
        {
            if (scale <= 0) scale = 1.0;
            var a = new LayoutRect(anchorPx.X / scale, anchorPx.Y / scale,
                                   anchorPx.W / scale, anchorPx.H / scale);
            return Placement(a, width, height, workDip, gap, margin);
        }

        /// <summary>Position of a popover of the given size, as close to the anchor as the work area allows.</summary>
        public static LayoutRect Placement(LayoutRect anchor, double width, double height, LayoutRect work, double gap = 8, double margin = 4)
        {
            width = Math.Max(width, 1);
            height = Math.Max(height, 1);
            var innerW = Math.Max(work.W - 2 * margin, 1);
            var innerH = Math.Max(work.H - 2 * margin, 1);
            var w = Math.Min(width, innerW);
            var h = Math.Min(height, innerH);

            // Horizontally centred on the anchor, nudged inside the work area.
            var x = anchor.X + anchor.W / 2 - w / 2;
            x = Math.Max(work.X + margin, Math.Min(x, work.X + work.W - w - margin));

            // Above the anchor when the popover fits there (the tray and menu-bar case), else below.
            var above = anchor.Y - gap - h >= work.Y + margin;
            double y;
            if (h < innerH)
            {
                y = above ? anchor.Y - gap - h : anchor.Y + anchor.H + gap;
                y = Math.Max(work.Y + margin, Math.Min(y, work.Y + work.H - h - margin));
            }
            else
            {
                // Nothing fits: fill the usable height and centre it, rather than jamming the
                // popover against the top edge of a screen that is shorter than its content.
                h = innerH;
                y = work.Y + (work.H - h) / 2;
            }
            return new LayoutRect(x, y, w, h);
        }

        /// <summary>Keep a window inside the work area, shrinking it only when it is too large.</summary>
        public static LayoutRect Contain(LayoutRect frame, LayoutRect work)
        {
            var w = Math.Min(frame.W, work.W);
            var h = Math.Min(frame.H, work.H);
            return new LayoutRect(
                Math.Min(Math.Max(frame.X, work.X), work.Right - w),
                Math.Min(Math.Max(frame.Y, work.Y), work.Bottom - h),
                w, h);
        }

        /// <summary>Preferred size, reduced so it still leaves `margin` inside the work area.</summary>
        public static (double W, double H) Fit(double preferredW, double preferredH, LayoutRect work, double margin = 12)
        {
            return (Math.Min(preferredW, Math.Max(work.W - 2 * margin, 1)),
                    Math.Min(preferredH, Math.Max(work.H - 2 * margin, 1)));
        }

        /// <summary>Top-left that centres a `w` x `h` window in the work area, clamped inside it.</summary>
        public static (double X, double Y) Center(double w, double h, LayoutRect work, double margin = 4)
        {
            var f = Contain(new LayoutRect(work.X + (work.W - w) / 2, work.Y + (work.H - h) / 2, w, h), work);
            return (f.X, f.Y);
        }
    }
}

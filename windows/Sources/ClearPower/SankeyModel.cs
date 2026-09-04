// Sankey data model + easing, ported from the top half of sankey.js (everything above
// _draw) via macos/Sources/ClearPowerApp/SankeyModel.swift.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ClearPower.Core;

namespace ClearPower.App
{
    public static class SankeyPalette
    {
        public static Color Rgb(double r, double g, double b) => Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        public static readonly Color Adapter = Rgb(0.36, 0.60, 0.92);
        public static readonly Color Battery = Rgb(0.40, 0.78, 0.52);
        public static readonly Color Pc = Rgb(0.58, 0.62, 0.68);
        public static readonly Color Cpu = Rgb(0.60, 0.48, 0.92);
        public static readonly Color Gpu = Rgb(0.86, 0.45, 0.78);
        public static readonly Color Soc = Rgb(0.45, 0.55, 0.95);
        public static readonly Color Mem = Rgb(0.55, 0.62, 0.78);
        public static readonly Color Disp = Rgb(0.92, 0.66, 0.34);
        public static readonly Color Other = Rgb(0.30, 0.74, 0.72);
    }

    public sealed class Sink
    {
        public string Id = "";
        public string Key = "";
        public string Label = "";
        public Color Color;
        public bool Approx;
        public Sink Clone() => (Sink)MemberwiseClone();
    }

    public sealed class SankeyNode
    {
        public string Id = "", Label = "", LabelKey = "";
        public double W;
        public Color Color;
        public bool Approx;
        public double InTot, OutTot, InOff, OutOff;
        public double X, Y, WPx, H;
    }

    public struct SankeyFlow { public string A, B; public double W; }

    public sealed class SankeyGraph
    {
        public List<SankeyNode>[] Cols = { new List<SankeyNode>(), new List<SankeyNode>(), new List<SankeyNode>() };
        public Dictionary<string, SankeyNode> Nodes = new Dictionary<string, SankeyNode>();
        public List<string> Order = new List<string>();
        public List<SankeyFlow> Flows = new List<SankeyFlow>();
        public double Scale = 1;
        public IEnumerable<SankeyNode> Ordered => Order.Select(id => Nodes[id]);
    }

    public sealed class SankeyModel
    {
        public static readonly Sink[] Sinks =
        {
            new Sink { Id = "cpu", Key = "cpu_w", Label = "cpu", Color = SankeyPalette.Cpu },
            new Sink { Id = "gpu", Key = "gpu_w", Label = "gpu", Color = SankeyPalette.Gpu },
            new Sink { Id = "soc", Key = "soc_w", Label = "soc", Color = SankeyPalette.Soc },
            new Sink { Id = "mem", Key = "mem_w", Label = "memory", Color = SankeyPalette.Mem },
            new Sink { Id = "disp", Key = "display_w", Label = "display", Color = SankeyPalette.Disp, Approx = true },
            new Sink { Id = "other", Key = "other_w", Label = "other", Color = SankeyPalette.Other },
        };
        public static readonly string[] Numeric = new[] { "sys_w", "bat_w", "adapter_w" }.Concat(Sinks.Select(s => s.Key)).ToArray();

        public const double Fps = 15;            // upper bound while the popover is open
        public const double Lerp = 0.18;         // per-frame easing towards the latest sample
        public const double SheenSpeed = 0.28;   // band sheen cycles per second
        public const double MinSinkW = 0.1;      // sinks below this are folded into "other" and hidden
        public const double NodeH = 50, Gap = 10, Pad = 6;

        public Dictionary<string, object?>? Target { get; private set; }
        public Dictionary<string, object?>? Shown { get; private set; }
        public double Phase { get; private set; }
        public double Height { get; private set; } = 3 * NodeH + 2 * Gap + 2 * Pad;
        public string FlowMode { get; set; } = "on-ac";
        public Func<bool> ReduceMotion { get; set; } = () => false;

        /// <summary>Tooltip description key for a node.</summary>
        public static string TipKey(SankeyNode n)
        {
            switch (n.Id)
            {
                case "adapter": return "tipAdapter";
                case "battery": return "tipBattery";
                case "batchg": return "tipBatchg";
                case "pc": return "tipSystem";
                case "other": return n.LabelKey == "displayOther" ? "tipDisplayOther" : "tipOther";
                case "disp": return "tipDisplay";
                case "mem": return "tipMemory";
                case "cpu": return "tipCpu";
                case "gpu": return "tipGpu";
                case "soc": return "tipSoc";
                default: return n.LabelKey == "system" ? "tipSystem" : "tipOther";
            }
        }

        /// <summary>New data. Eases in while visible, snaps otherwise. Returns true when a frame timer should run.</summary>
        public bool Update(Dictionary<string, object?> snap, bool active)
        {
            Target = snap;
            if (Shown == null)
            {
                Shown = new Dictionary<string, object?>(snap);
            }
            else
            {
                foreach (var kv in snap)
                    if (!Numeric.Contains(kv.Key) || (Snapshot.Double(kv.Value) ?? -1) < 0) Shown[kv.Key] = kv.Value;  // -1 snaps instantly
            }
            FitHeight();
            if (!active)
            {
                foreach (var k in Numeric) Shown[k] = snap.TryGetValue(k, out var v) ? v : null;
            }
            return active;
        }

        public bool SheenEnabled()
        {
            if (ReduceMotion() || FlowMode == "never") return false;
            if (FlowMode == "on-ac") return Target?.B("on_ac") ?? false;
            return true;
        }

        /// <summary>Advance one frame; returns true while something is still moving or the sheen runs.</summary>
        public bool Frame(double dt)
        {
            var moving = false;
            if (Target != null && Shown != null)
            {
                foreach (var k in Numeric)
                {
                    var tv = Target.D(k, 0);
                    if (tv < 0) { Shown[k] = tv; continue; }
                    var sv0 = Shown.D(k, tv);
                    var sv = sv0 < 0 ? tv : sv0;
                    var n = sv + (tv - sv) * Lerp;
                    if (Math.Abs(tv - n) > 0.005) { Shown[k] = n; moving = true; } else Shown[k] = tv;
                }
            }
            var sheen = SheenEnabled();
            if (sheen) Phase = (Phase + dt * SheenSpeed) % 1;
            return sheen || moving;
        }

        /// <summary>Which sinks are visible is decided on the *target* values so bands never flicker.</summary>
        public List<Sink> VisibleSinks()
        {
            var tg = Target;
            if (tg == null) return new List<Sink>();
            var measured = tg.D("cpu_w") >= 0;
            var displayKnown = tg.D("display_w") >= 0;
            if (!measured)  // no per-block counters: everything we know is the total
            {
                var s = Sinks[5].Clone(); s.Key = "sys_w"; s.Label = "system";
                return new List<Sink> { s };
            }
            var vis = Sinks.Where(s => !(s.Id == "disp" && !displayKnown) && tg.D(s.Key) >= MinSinkW).Select(s => s.Clone()).ToList();
            if (!vis.Any(s => s.Id == "other")) vis.Add(Sinks[5].Clone());
            foreach (var s in vis)
                if (s.Id == "other" && !displayKnown) s.Label = "displayOther";
            return vis;
        }

        private void FitHeight()
        {
            var n = Math.Max(3, VisibleSinks().Count);
            Height = n * NodeH + (n - 1) * Gap + 2 * Pad;
        }

        public SankeyGraph Model(Dictionary<string, object?> s)
        {
            var onAc = s.B("on_ac");
            var batW = s.D("bat_w", 0);
            var sysW = Math.Max(s.D("sys_w", 0), 0);
            var g = new SankeyGraph();
            SankeyNode Add(int col, string id, string label, double w, Color color, string key = "")
            {
                var n = new SankeyNode { Id = id, Label = label, W = w, Color = color, LabelKey = key == "" ? id : key };
                g.Nodes[id] = n; g.Order.Add(id); g.Cols[col].Add(n);
                return n;
            }
            void Flow(string a, string b, double w)
            {
                if (w > 0.005) g.Flows.Add(new SankeyFlow { A = a, B = b, W = w });
            }
            var estimate = s.S("sys_source") == "estimate";
            if (onAc)
            {
                var fromBat = -batW >= 0.05 ? -batW : 0;
                var toBat = batW >= 0.05 ? batW : 0;
                var adToPc = Math.Max(sysW - fromBat, 0);
                Add(0, "adapter", I18n.T("adapter"), adToPc + toBat, SankeyPalette.Adapter).Approx = estimate;
                if (fromBat > 0) Add(0, "battery", I18n.T("battery"), fromBat, SankeyPalette.Battery);
                if (toBat > 0) Add(1, "batchg", I18n.T("battery"), toBat, SankeyPalette.Battery);
                Add(1, "pc", I18n.T("system"), sysW, SankeyPalette.Pc).Approx = estimate;
                Flow("adapter", "batchg", toBat);
                Flow("adapter", "pc", adToPc);
                Flow("battery", "pc", fromBat);
            }
            else
            {
                Add(0, "battery", I18n.T("battery"), sysW, SankeyPalette.Battery);
                Add(1, "pc", I18n.T("system"), sysW, SankeyPalette.Pc);
                Flow("battery", "pc", sysW);
            }
            // Sinks: eased values, hidden ones folded into "other", then normalised so that
            // they add up exactly to the eased total.
            var vis = VisibleSinks();
            var visIds = new HashSet<string>(vis.Select(v => v.Id));
            var hidden = 0.0;
            foreach (var sk in Sinks)
                if (!visIds.Contains(sk.Id) && s.D(sk.Key) > 0 && (Target?.D("cpu_w") ?? -1) >= 0) hidden += s.D(sk.Key);
            var vals = vis.Select(v => Math.Max(s.D(v.Key, 0), 0) + (v.Id == "other" ? hidden : 0)).ToList();
            var sum = vals.Sum();
            var k = (sum > 0.01 && sysW > 0) ? sysW / sum : 1;
            for (int i = 0; i < vis.Count; i++)
            {
                var v = vis[i];
                var n = Add(2, v.Id, I18n.T(v.Label), vals[i] * k, v.Color, v.Label);
                n.Approx = v.Approx;
                Flow("pc", v.Id, vals[i] * k);
            }
            foreach (var f in g.Flows)
            {
                g.Nodes[f.A].OutTot += f.W;
                g.Nodes[f.B].InTot += f.W;
            }
            return g;
        }

        public void Layout(SankeyGraph g, double W, double H)
        {
            var colW = new[] { 64.0, 64.0, 78.0 };
            var colX = new[] { Pad, Math.Round(W / 2 - colW[1] / 2), W - Pad - colW[2] };
            var scale = double.PositiveInfinity;
            foreach (var col in g.Cols)
            {
                var tot = col.Sum(n => n.W);
                var avail = H - 2 * Pad - Gap * (col.Count - 1);
                if (tot > 0) scale = Math.Min(scale, avail / tot);
            }
            if (double.IsInfinity(scale) || double.IsNaN(scale)) scale = 1;
            for (int pass = 0; pass < 6; pass++)
            {
                var ok = true;
                foreach (var col in g.Cols)
                {
                    var avail = H - 2 * Pad - Gap * (col.Count - 1);
                    var need = col.Sum(n => Math.Max(NodeH, n.W * scale));
                    if (need > avail + 0.5) { scale *= avail / need; ok = false; }
                }
                if (ok) break;
            }
            for (int ci = 0; ci < 3; ci++)
            {
                var col = g.Cols[ci];
                var hs = col.Select(n => Math.Max(NodeH, n.W * scale)).ToList();
                var total = hs.Sum() + Gap * (col.Count - 1);
                var y = (H - total) / 2;
                for (int i = 0; i < col.Count; i++)
                {
                    var n = col[i];
                    n.X = colX[ci]; n.Y = y; n.WPx = colW[ci]; n.H = hs[i];
                    n.InOff = (n.H - n.InTot * scale) / 2;
                    n.OutOff = (n.H - n.OutTot * scale) / 2;
                    y += hs[i] + Gap;
                }
            }
            g.Scale = scale;
        }
    }
}

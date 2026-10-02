// Golden tests: the Python daemon (daemon/clearpowerd) is the reference. Fixtures are
// produced by macos/scripts/gen-fixtures.py (shared with the Swift port) and compared
// value by value. Mirrors macos/Tests/ClearPowerCoreTests/GoldenTests.swift.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClearPower.Core;
using ClearPower.Win;
using Xunit;

namespace ClearPower.Core.Tests
{
    internal static class Fx
    {
        public static object? Load(string name)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json");
            return Json.Parse(File.ReadAllText(path));
        }

        public static List<Dictionary<string, object?>> List(string name)
            => ((List<object?>)Load(name)!).Cast<Dictionary<string, object?>>().ToList();

        public static Dictionary<string, object?> Obj(string name) => (Dictionary<string, object?>)Load(name)!;

        public static void Near(double a, double b, double tol = 1e-9, string msg = "")
        {
            var ok = Math.Abs(a - b) <= Math.Max(tol, Math.Abs(b) * 1e-9);
            Assert.True(ok, $"{msg}: got {a}, expected {b}");
        }

        public static List<Dictionary<string, object?>> Dicts(object? v) => ((List<object?>)v!).Cast<Dictionary<string, object?>>().ToList();
        public static List<object?> Arr(object? v) => (List<object?>)v!;
    }

    public class EmaGoldenTests
    {
        [Fact]
        public void Ema()
        {
            foreach (var c in Fx.List("ema"))
            {
                var e = new Ema(c.D("tau"));
                var steps = Fx.Dicts(c["steps"]);
                for (int i = 0; i < steps.Count; i++)
                {
                    if (i == steps.Count - 1) e.Reset();
                    var v = e.Update(steps[i].D("x"), steps[i].D("t"));
                    Fx.Near(v, steps[i].D("v"), 1e-9, $"tau {c.D("tau")} step {i}");
                }
            }
        }
    }

    public class RuntimeGoldenTests
    {
        [Fact]
        public void Runtime()
        {
            var rt = new RuntimeEstimator();
            // Fixture only records every 7th step (plus the first 7); replay the same plan.
            double t = 0, e = 60;
            var plan = new (string status, double w, int n)[] { ("Discharging", 12, 180), ("Charging", -30, 120), ("Not charging", 0, 12), ("Discharging", 8, 60) };
            var recorded = Fx.List("runtime");
            int ri = 0, i = 0;
            foreach (var (status, w, n) in plan)
            {
                for (int k = 0; k < n; k++)
                {
                    t += 10; e -= w * 10 / 3600;
                    rt.Add(t, e, status);
                    i++;
                    if (i % 7 == 0 || i < 8)
                    {
                        var r = recorded[ri++];
                        Fx.Near(t, r.D("t"));
                        var outp = rt.Estimate(e, r.D("target_wh"), r.D("fallback_w"));
                        var exp = (Dictionary<string, object?>)r["out"]!;
                        foreach (var kv in exp)
                            Fx.Near(Snapshot.Double(outp[kv.Key])!.Value, Snapshot.Double(kv.Value)!.Value, 1e-6, $"step {i} key {kv.Key}");
                    }
                }
            }
            Assert.Equal(recorded.Count, ri);
        }
    }

    public class PowerModelGoldenTests
    {
        [Fact]
        public void Breakdown()
        {
            var m = new PowerModel(5);
            var cases = Fx.List("power_model");
            for (int i = 0; i < cases.Count; i++)
            {
                var c = cases[i];
                var inp = (Dictionary<string, object?>)c["in"]!;
                var raw = new RawPower(inp.D("bat_w"), inp.D("psys"), inp.D("package"), inp.D("core"), inp.D("uncore"), inp.D("dram"));
                var outp = m.Update(raw, inp.B("on_ac"), c.D("t"), inp.D("emission"), inp.B("display_on"));
                Fx.Near(m.Raw.Rest, c.D("raw_rest"), 1e-9, $"step {i} rest");
                foreach (var kv in (Dictionary<string, object?>)c["out"]!)
                {
                    if (kv.Value is string s) Assert.True(outp[kv.Key] as string == s, $"step {i} {kv.Key}");
                    else Fx.Near(Snapshot.Double(outp[kv.Key])!.Value, Snapshot.Double(kv.Value)!.Value, 1e-9, $"step {i} {kv.Key}");
                }
            }
        }
    }

    internal sealed class FakeChargeHW : IChargeHardware
    {
        public bool ThresholdsSupported => true;
        public IReadOnlyList<string> Behaviours => new[] { "auto", "inhibit-charge", "force-discharge" };
        public List<string> Writes = new List<string>();
        public int? Saved;
        public void WriteThresholds(int start, int end)
        {
            // Linux writes them in an order dictated by sysfs; the fixture is normalised below.
            Writes.Add($"start:{start}"); Writes.Add($"end:{end}");
        }
        public void WriteBehaviour(string behaviour) => Writes.Add($"beh:{behaviour}");
        public int? LoadLimit() => null;
        public void SaveLimit(int limit) => Saved = limit;
    }

    public class ChargeGoldenTests
    {
        [Fact]
        public void StateMachine()
        {
            var hw = new FakeChargeHW();
            var c = new ChargeStateMachine(hw, 20);
            foreach (var step in Fx.List("charge"))
            {
                var op = step.S("op");
                var args = (Dictionary<string, object?>)step["args"]!;
                switch (op)
                {
                    case "startup": c.ApplyStartup(); break;
                    case "set_limit": c.SetLimit(args.I("pct")); break;
                    case "start_topup": c.StartTopUp(); break;
                    case "start_discharge": c.StartDischarge(args.I("target")); break;
                    case "cancel": c.Cancel(); break;
                    case "shutdown": c.Shutdown(); break;
                    case "tick": c.Tick(args.I("pct"), args.S("status")); break;
                    default: Assert.Fail($"unknown op {op}"); break;
                }
                var st = (Dictionary<string, object?>)step["state"]!;
                Assert.True(c.Mode.Raw() == st.S("charge_mode"), op);
                Assert.True(c.Limit == st.I("charge_limit"), op);
                Assert.True(c.Target == st.I("charge_target"), op);
                // Compare the set of writes ignoring the sysfs-specific threshold ordering.
                var expected = Fx.Arr(step["writes"]).Select(w => { var a = Fx.Arr(w); return $"{a[0]}:{FmtVal(a[1])}"; }).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var got = hw.Writes.OrderBy(x => x, StringComparer.Ordinal).ToList();
                Assert.True(expected.SequenceEqual(got), $"{op}: expected [{string.Join(",", expected)}] got [{string.Join(",", got)}]");
                hw.Writes.Clear();
            }
        }

        private static string FmtVal(object? v) => v is double d ? ((long)d).ToString() : (v?.ToString() ?? "");
    }

    internal sealed class FakeBrightness : IBrightnessControl
    {
        public bool Available => true;
        public int Max => 1000;
        public int Value = 700;
        public List<int> Sets = new List<int>();
        public int? ReadRaw() => Value;
        public void SetRaw(int v) { Value = v; Sets.Add(v); }
    }

    public class DisplayCalGoldenTests
    {
        [Fact]
        public void SweepAndInterpolation()
        {
            var fx = Fx.Obj("display_cal");
            var bl = new FakeBrightness();
            var d = new DisplayCalibration(bl, null);
            var now = 100.0;
            d.Start(now, true);
            Assert.Equal("running", d.State);
            int guard = 0;
            while (d.State == "running" && guard < 10000)
            {
                now += 0.5;
                var lvl = bl.Value / 1000.0;
                var noise = new[] { 0.3, -0.2, 0.1, 0.0, -0.1 }[(int)(now * 2) % 5];
                d.Tick(3.0 + 6.0 * lvl + noise, now);
                guard++;
            }
            Assert.Equal(fx.S("final_state"), d.State);
            Assert.Equal(Fx.Arr(fx["sets"]).Select(v => (int)(double)v!).ToList(), bl.Sets);
            var table = Fx.Arr(fx["table"]).Select(Fx.Arr).ToList();
            Assert.Equal(table.Count, d.Table.Count);
            for (int i = 0; i < table.Count; i++)
            {
                Assert.Equal((double)table[i][0]!, (double)d.Table[i].raw);
                Fx.Near(d.Table[i].w, (double)table[i][1]!, 1e-9);
            }
            Fx.Near(d.Rest0, fx.D("rest0"), 1e-9);
            foreach (var c in Fx.Dicts(fx["interp"]))
            {
                var raw = c.I("raw");
                var apl = c.D("apl");
                if (c.ContainsKey("w"))
                {
                    if (apl >= 0) d.SetContent(apl, now); else d.SetContent(-1, now - 100);
                    Fx.Near(d.EmissionW(raw, now), c.D("w"), 1e-9, $"raw {raw} apl {apl}");
                }
                else if (c.ContainsKey("w_stale"))
                {
                    d.SetContent(apl, now);
                    Fx.Near(d.EmissionW(raw, now + 61), c.D("w_stale"), 1e-9, "stale");
                }
            }
        }
    }

    public class HistoryGoldenTests
    {
        [Fact]
        public void Downsampling()
        {
            var fx = Fx.Obj("history");
            var h = new History(120, 10);
            foreach (var s in Fx.Dicts(fx["snaps"])) h.Add(s);
            var get = (Dictionary<string, object?>)fx["get"]!;
            foreach (var f in new[] { "sys_w", "soc_w", "bat_pct" })
            {
                var exp = Fx.Arr(get[f]).Select(Fx.Arr).ToList();
                var got = h.Get(f, 60);
                Assert.True(got.Count == exp.Count, f);
                for (int i = 0; i < exp.Count; i++)
                {
                    Fx.Near(got[i].t, (double)exp[i][0]!);
                    Fx.Near(got[i].v, (double)exp[i][1]!, 1e-9, f);
                }
            }
            var all = Fx.Arr(get["all_sys"]);
            Assert.Equal(all.Count, h.Get("sys_w", 1e9).Count);
        }
    }

    public class I18nTests
    {
        [Fact]
        public void ResolveAndFormat()
        {
            Assert.Equal("zh_CN", I18n.ResolveLanguage("system", new[] { "zh-Hans-CN", "en" }));
            Assert.Equal("en", I18n.ResolveLanguage("system", new[] { "en-US" }));
            Assert.Equal("zh_CN", I18n.ResolveLanguage("zh-cn", new[] { "en" }));
            I18n.SetLanguage("en", new string[0]);
            Assert.Equal("Limit 80%", I18n.T("limit", "n", 80));
            Assert.Equal("2 h 5 m", I18n.FmtDuration(125));
            Assert.Equal("12 min", I18n.FmtDuration(12.4));
            I18n.SetLanguage("zh-cn", new string[0]);
            Assert.Equal("2 小时 5 分", I18n.FmtDuration(125));
            Assert.Equal("missingKey", I18n.T("missingKey"));
            var en = new HashSet<string>(I18n.Strings["en"].Keys);
            var zh = new HashSet<string>(I18n.Strings["zh_CN"].Keys);
            Assert.True(en.SetEquals(zh), "zh/en key sets differ: " + string.Join(",", en.Except(zh).Concat(zh.Except(en))));
        }
    }

    public class ProcessBudgetTests
    {
        /// <summary>Busy core count, for the samples that are not idle.</summary>
        private const double Busy = 1.05;

        [Fact]
        public void BudgetDistribution()
        {
            var pb = new ProcessBudget(3, 600);
            pb.Sample(0, 4, 0.0, () => new (string, double)[0]);   // establishes the floor
            var top = pb.Sample(10, 10, Busy, () => new[] { ("a", 50.0), ("b", 25.0), ("a", 25.0), ("c", 0.0), ("d", 5.0) });
            // floor is the 20th percentile of {4, 10} = 4, budget 6 W; a=75 of 105
            Assert.Equal(3, top.Count);
            Assert.Equal("a", top[0].name); Fx.Near(top[0].w, 6 * 75.0 / 105.0, 1e-9);
            Assert.Equal("b", top[1].name); Assert.Equal("d", top[2].name);
            // Called again within interval -> cached
            Assert.Equal("a", pb.Sample(11, 20, Busy, () => new[] { ("z", 1.0) }).First().name);
        }

        /// <summary>
        /// Regression: the floor used to be the raw minimum of the window, so one low reading pinned
        /// it and - just as bad - a machine that never goes properly idle put the floor at the lowest
        /// sample of ten minutes, well below what "idle plus the usual background" actually costs.
        /// The floor is now a low percentile, so an isolated load spike cannot drag it up.
        /// </summary>
        [Fact]
        public void LoadSpikeDoesNotDragTheFloorUp()
        {
            var pb = new ProcessBudget(1, 600);
            var usage = new[] { ("app", 100.0) };
            // Timed 2 s apart so every call gets past the 1 s sampling interval.
            double[] samples = { 4.0, 4.2, 20.0, 4.1 };      // one burst of load among idle readings
            for (int i = 0; i < samples.Length; i++) pb.Sample(i * 2, samples[i], 1.0, () => usage);
            // Five readings, sorted {4.0, 4.1, 4.1, 4.2, 20}; 20 % of 5 truncates to index 1 -> floor
            // 4.1, nowhere near the 20 W spike, so the budget is 6 - 4.1 = 1.9 W.
            var top = pb.Sample(20, 6, 1.0, () => usage);
            Assert.Single(top);
            Assert.True(Math.Abs(top[0].w - 1.9) < 1e-9, $"budget = 6 - floor(4.1): got {top[0].w}");
        }

        /// <summary>
        /// The floor tracks the quiet end of the readings rather than their average, so a handful of
        /// idle samples is enough to establish a sensible baseline.
        /// </summary>
        [Fact]
        public void FloorTracksTheQuietEndNotTheAverage()
        {
            var pb = new ProcessBudget(1, 600);
            var usage = new[] { ("app", 100.0) };
            double[] idle = { 3.0, 3.2, 3.1, 3.3, 3.15 };
            for (int i = 0; i < idle.Length; i++) pb.Sample(i * 2, idle[i], 0.2, () => usage);
            // Sorted: {3.0, 3.1, 3.15, 3.2, 3.3}; 20 % of 5 = index 1 -> floor 3.1 (not the 3.15 mean).
            var top = pb.Sample(20, 8, 3.0, () => usage);
            Assert.Single(top);
            Assert.True(Math.Abs(top[0].w - 4.9) < 1e-9, $"budget = 8 - floor(3.1): got {top[0].w}");
        }

        /// <summary>
        /// Regression: an unknown package power used to be recorded as a 0 W floor entry. Because
        /// the floor is a rolling statistic over ten minutes, that single entry made every later
        /// budget the full package power, and it could not age out for the whole window.
        /// </summary>
        [Fact]
        public void UnknownPackagePowerDoesNotPoisonTheFloor()
        {
            var pb = new ProcessBudget(3, 600);
            var usage = new[] { ("app", 100.0) };

            // -1 = RAPL/Energy Meter not ready yet (or the first sample after resume). It must not
            // be recorded: the previous (empty) result comes back and the floor stays untouched.
            Assert.Empty(pb.Sample(0, -1, 0.0, () => usage));

            // A real reading establishes the floor: budget = 7 - 7 = 0, so the app is listed at 0 W.
            var first = pb.Sample(1, 7, 0.0, () => usage);
            Assert.Single(first);
            Fx.Near(first[0].w, 0.0, 1e-9, "the first real reading is its own floor");

            // A later rise is attributed as the *difference*: 8 - 7 = 1 W, not 8 W. Without the fix
            // the poisoned 0 W floor would have made this the whole 8 W. (t=4 is past the 3 s
            // interval the t=1 sample opened; an earlier call would just return the cached result.)
            var top = pb.Sample(4, 8, 0.0, () => usage);
            Assert.Single(top);
            Fx.Near(top[0].w, 1.0, 1e-9, "budget must be package_w - floor");
        }

        /// <summary>
        /// Regression: an unusable sample used to consume a whole sampling interval, so the next
        /// attempt was three seconds away even though nothing had been computed.
        /// </summary>
        [Fact]
        public void UnknownPackagePowerDoesNotConsumeTheInterval()
        {
            var pb = new ProcessBudget(3, 600);
            var usage = new[] { ("app", 100.0) };
            // t=0: floor 1, budget 0 -> the app is listed at 0 W and the interval opens until t=3.
            var baseline = pb.Sample(0, 1, 0.0, () => usage);
            Assert.Single(baseline);
            Fx.Near(baseline[0].w, 0.0, 1e-9, "the first reading is its own floor");

            // t=1: unknown power. The cached row comes back, and the interval must stay as it was —
            // the old code consumed it here, pushing the next real attempt out to t=4 instead of t=3.
            var skipped = pb.Sample(1, -1, 0.0, () => usage);
            Assert.Single(skipped);
            Fx.Near(skipped[0].w, 0.0, 1e-9, "an unusable sample must not fabricate a new result");

            // t=3: the interval the t=0 sample opened has now elapsed, so a real reading is taken.
            var top = pb.Sample(3, 2, 0.0, () => usage);
            Assert.Single(top);
            Fx.Near(top[0].w, 1.0, 1e-9, "the skipped sample must not have extended the interval");
        }
    }

    public class EmaSettleTests
    {
        /// <summary>
        /// Regression: the per-application attribution subtracts a ten-minute *minimum* of package
        /// power, so a sample taken while the average is still ramping used to become the floor.
        /// On Windows the first Energy Meter read is unavailable and the following reads ramp up
        /// from it, which pinned the floor near zero and hid every application.
        /// </summary>
        [Fact]
        public void SettledOnlyAfterOneTimeConstant()
        {
            var e = new Ema(5.0);
            Assert.False(e.Settled);
            e.Update(7.0, 0);
            Assert.False(e.Settled);          // first value is not yet an average of anything
            e.Update(8.0, 1);
            Assert.False(e.Settled);
            e.Update(8.0, 6);
            Assert.True(e.Settled);           // one tau of real samples has passed
            e.Reset();
            Assert.False(e.Settled);          // resume from sleep ramps again
        }

        /// <summary>
        /// End-to-end guard for the Windows path: right after construction, and for as long as the
        /// package average is still ramping, the attribution must be told "unknown" rather than be
        /// handed a number that would be pinned as the ten-minute floor.
        /// </summary>
        [Fact]
        public void PackageForBudgetStaysUnknownWhileRamping()
        {
            var s = new Sampler(5.0, null);
            Assert.Equal(-1.0, s.PackageForBudget, 9);   // nothing sampled yet
            s.Sample();
            Assert.Equal(-1.0, s.PackageForBudget, 9);   // a single value is not yet an average
            for (int i = 0; i < 3; i++)
            {
                System.Threading.Thread.Sleep(600);
                s.Sample();
            }
            Assert.Equal(-1.0, s.PackageForBudget, 9);   // still inside the first time constant
        }
    }

    /// <summary>
    /// The popover anchoring arithmetic with the numbers measured on the development machine: a
    /// 2880x1800 panel at 225 %. GetMonitorInfo and GetDpiForMonitor report the *native* work area
    /// of 2880x1692 at 2.25, which is 1280x752 layout units - while SystemParameters.WorkArea
    /// reports that very same "1280x752" as if it were already layout units, i.e. 2.25x too small
    /// in each direction. Anchoring the popover to that box is what made it drift.
    /// </summary>
    public class WindowGeometryTests
    {
        /// <summary>The real usable area in layout units: 2880x1692 native / 2.25.</summary>
        private static readonly LayoutRect Work = new LayoutRect(0, 0, 1280, 752);

        /// <summary>The tray icon as the app receives it from Shell_NotifyIconGetRect and then
        /// divides by that monitor's scale, which is what PopoverWindow.Place does.</summary>
        private static readonly LayoutRect Tray = new LayoutRect(1180 / 2.25, 1692 / 2.25, 20 / 2.25, 20 / 2.25);

        /// <summary>Absolute comparison: Fx.Near is relative, far too loose for pixel geometry.</summary>
        private static void Near(double actual, double expected, double tol, string msg = "")
            => Assert.True(Math.Abs(actual - expected) <= tol, $"{msg}: got {actual}, expected {expected}");

        [Fact]
        public void PopoverSitsAboveTheTrayAndInsideTheWorkArea()
        {
            var p = WindowGeometry.Placement(Tray, 400, 230, Work);
            Near(p.Y + p.H, Tray.Y - 8, 0.001, "rests just above the icon");
            Near(p.X, Tray.X + Tray.W / 2 - 200, 0.001, "centred on the anchor");
            Assert.True(p.X >= Work.X && p.X + p.W <= Work.Right + 0.001, "horizontally inside");
            Assert.True(p.Y >= Work.Y && p.Y + p.H <= Work.Bottom + 0.001, "vertically inside");
        }

        /// <summary>
        /// Regression for the reported drift: the old code clamped against SystemParameters.WorkArea
        /// (1280x752 raw pixels standing in for a 2880x1692 native work area) and transformed the
        /// tray rect with whatever DPI the window was already on. Here that put the popover at
        /// 440,556 instead of the correct 328.9,514 - and worse on a secondary display.
        /// </summary>
        [Fact]
        public void PopoverIsAnchoredInTheTargetMonitorsUnitsNotTheWindowsCurrentOnes()
        {
            // The 225 % primary display: native work area 2880x1692, physical icon at 1180,1692.
            var p = WindowGeometry.PlacementForPhysicalAnchor(
                new LayoutRect(1180, 1692, 20, 20), 400, 230, new LayoutRect(0, 0, 1280, 752), 2.25);
            Near(p.X, 328.8888888888889, 0.001, "centred on the icon in layout units");
            Near(p.Y, 514.0, 0.001, "directly above the icon, inside the work area");
        }

        /// <summary>
        /// The cross-monitor case the old code could not represent at all: the same window being
        /// placed on a 100 % secondary display, whose work area has a negative origin. The identity
        /// of the monitor decides the conversion, not where the popover happens to be right now.
        /// </summary>
        [Fact]
        public void SecondDisplayAtDifferentScaleUsesItsOwnConversion()
        {
            var work = new LayoutRect(-1920, 0, 1920, 1040);        // secondary, 100 %, left of primary
            var p = WindowGeometry.PlacementForPhysicalAnchor(
                new LayoutRect(-900, 1010, 24, 24), 400, 230, work, 1.0);
            Near(p.X, -900 + 12 - 200, 0.001, "centred on the physical icon at 100 %");
            Near(p.Y + p.H, 1010 - 8, 0.001, "anchored above it");
            Assert.True(p.X >= work.X && p.X + p.W <= work.Right + 0.001, "inside the secondary display");
        }

        /// <summary>
        /// A secondary display at 150 % to the right of the primary: a 400-unit popover is 600
        /// native pixels wide there, and the anchor conversion must use 1.5 rather than the
        /// primary's scale or 1.0.
        /// </summary>
        [Fact]
        public void ScaledSecondaryDisplayConvertsWithItsOwnFactor()
        {
            // A 2560x1440 secondary display at 150 % sits to the right of a 1280-unit primary, so
            // its work area starts at x=1280 and is 1706.67 x 960 units.
            var work = new LayoutRect(1280, 0, 1706.67, 960);
            // Native pixels on that screen: icon near its bottom-right, taskbar 80 px tall.
            var p = WindowGeometry.PlacementForPhysicalAnchor(
                new LayoutRect(2400, 1360, 30, 30), 400, 230, work, 1.5);
            // The anchor is (1600, 906.67) in layout units with a 20-unit width, so centring gives
            // 1600 + 10 - 200 = 1410 - comfortably inside the 1280..2986.67 work area.
            Near(p.X, 1410, 0.001, "converted with the 150 % scale, then centred on the icon");
            Near(p.X, 2400 / 1.5 + (30 / 1.5) / 2 - 400.0 / 2, 0.001, "matches the per-monitor conversion");
            Near(p.Y + p.H, 1360 / 1.5 - 8, 0.001, "anchored above the icon in that display's units");
            Assert.True(p.X >= work.X && p.X + p.W <= work.Right + 0.001, "inside that display");
            Assert.True(p.Y >= work.Y && p.Y + p.H <= work.Bottom + 0.001, "vertically inside");
        }

        /// <summary>A display to the left of the primary one has negative coordinates.</summary>
        [Fact]
        public void NegativeOriginWorkAreaIsRespected()
        {
            var work = new LayoutRect(-1920, 0, 1920, 1040);
            var anchor = new LayoutRect(-1700, 1000, 20, 20);
            var p = WindowGeometry.Placement(anchor, 400, 300, work);
            Assert.True(p.X >= work.X, "not pushed onto the primary display");
            Assert.True(p.Y >= work.Y && p.Y + p.H <= work.Bottom + 0.001, "vertically inside");
            Near(p.Y + p.H, anchor.Y - 8, 0.001, "still anchored above the icon");
            Near(p.X, -1700 + 10 - 200, 0.001, "still centred on the icon");
        }

        /// <summary>
        /// A secondary display with the same negative origin but a 100 % scale: the same physical
        /// anchor becomes a different layout position, which is exactly what the old
        /// TransformFromDevice got wrong when the window was on the other monitor.
        /// </summary>
        [Fact]
        public void LowerScaleMonitorKeepsTheSamePhysicalAnchor()
        {
            var work = new LayoutRect(-1920, 0, 1920, 1080);          // 100 % scale
            var physicalIcon = new LayoutRect(-900, 1040, 24, 24);     // native pixels
            var a = new LayoutRect(physicalIcon.X, physicalIcon.Y, physicalIcon.W, physicalIcon.H);
            var p = WindowGeometry.Placement(a, 400, 230, work);
            Near(p.Y + p.H, a.Y - 8, 0.001, "anchored above the same physical icon");
            Assert.True(p.X >= work.X && p.X + p.W <= work.Right + 0.001, "inside that monitor");
        }

        [Fact]
        public void PopoverTallerThanTheWorkAreaIsCentredAndShrunk()
        {
            var p = WindowGeometry.Placement(Tray, 400, 900, Work);
            Near(p.H, Work.H - 8, 0.001, "limited to the usable height");
            Near(p.Y, Work.Y + (Work.H - p.H) / 2, 0.001, "centred rather than jammed against the top edge");
            Assert.True(p.Y >= Work.Y && p.Y + p.H <= Work.Bottom + 0.001, "still inside");
        }

        /// <summary>A short screen (a 1366x768 panel at 125 %): 400 units do not fit beside the anchor.</summary>
        [Fact]
        public void PopoverWiderThanTheRemainingSpaceIsStillFullyOnScreen()
        {
            var work = new LayoutRect(0, 0, 1092, 582);
            var anchor = new LayoutRect(8, 560, 16, 16);              // tray at the far left
            var p = WindowGeometry.Placement(anchor, 400, 230, work);
            Assert.True(p.X >= work.X + 4 - 0.001, "not pushed off the left edge");
            Assert.True(p.X + p.W <= work.Right - 4 + 0.001, "not pushed off the right edge");
            Near(p.W, 400, 0.001, "the width is not silently changed");
        }

        [Fact]
        public void PopoverFallsBelowWhenThereIsNoRoomAbove()
        {
            var anchor = new LayoutRect(100, Work.Y + 6, 20, 20);   // near the top edge
            var p = WindowGeometry.Placement(anchor, 300, 100, Work);
            Near(p.Y, anchor.Bottom + 8, 0.001, "below the anchor when nothing fits above");
        }

        [Fact]
        public void ContainPullsAWindowBackFromARemovedDisplay()
        {
            var work = new LayoutRect(0, 0, 1920, 1040);
            var stranded = new LayoutRect(3000, -200, 600, 500);   // last seen on a display that is gone
            var c = WindowGeometry.Contain(stranded, work);
            Assert.True(c.X >= work.X && c.X + c.W <= work.Right + 0.001, "horizontally recovered");
            Assert.True(c.Y >= work.Y && c.Y + c.H <= work.Bottom + 0.001, "vertically recovered");
        }

        [Fact]
        public void ContainShrinksAWindowLargerThanTheDisplay()
        {
            var work = new LayoutRect(0, 0, 800, 600);
            var c = WindowGeometry.Contain(new LayoutRect(-50, -50, 1200, 900), work);
            Fx.Near(c.W, 800, 0.001);
            Fx.Near(c.H, 600, 0.001);
            Fx.Near(c.X, 0, 0.001);
            Fx.Near(c.Y, 0, 0.001);
        }

        [Fact]
        public void CenterKeepsTheSettingsWindowOnScreen()
        {
            var work = new LayoutRect(0, 0, 1280.0 / 2.25, 752.0 / 2.25);
            var (x, y) = WindowGeometry.Center(460, 334, work);
            Assert.True(x >= work.X && x + 460 <= work.Right + 0.001, "centred horizontally inside");
            Assert.True(y >= work.Y && y + 334 <= work.Bottom + 0.001, "centred vertically inside");
        }
    }

    /// <summary>
    /// The charge backend registry. Every backend is probed on every machine, so the contract that
    /// matters is that a probe is safe off its own vendor and always says why it declined - that is
    /// what makes the diagnostics output actionable when a machine is not covered.
    /// </summary>
    public class ChargeBackendTests
    {
        [Fact]
        public void EveryBackendReportsWhyItDeclined()
        {
            var reports = ChargeBackends.Probe();
            Assert.NotEmpty(reports);
            foreach (var r in reports)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Name), "backend has no name");
                Assert.False(string.IsNullOrWhiteSpace(r.Vendor), "backend has no vendor");
                Assert.False(string.IsNullOrWhiteSpace(r.Detail),
                    $"{r.Name} gave no reason; a probe must explain its outcome");
            }
        }

        [Fact]
        public void AtMostOneBackendClaimsTheMachine()
        {
            var reports = ChargeBackends.Probe();
            var ready = reports.Where(r => r.State == BackendState.Ready).ToList();
            Assert.True(ready.Count <= 1, $"{ready.Count} backends all claim this machine: " +
                string.Join(", ", ready.Select(r => r.Name)));
        }

        /// <summary>
        /// What Detect() returns has to agree with the probe, or the diagnostics would describe a
        /// different machine than the app runs on.
        /// </summary>
        [Fact]
        public void DetectAgreesWithTheProbe()
        {
            var ready = ChargeBackends.Probe().Any(r => r.State == BackendState.Ready);
            var hw = ChargeBackends.Detect(null);
            Assert.Equal(ready, hw.ThresholdsSupported);
        }
    }

    public class JsonTests
    {
        [Fact]
        public void RoundTrip()
        {
            var snap = new Dictionary<string, object?> { ["sys_w"] = 12.5, ["bat_pct"] = 80, ["on_ac"] = true, ["name"] = "屏幕 \"x\"", ["nan"] = double.NaN, ["list"] = new List<object?> { 1.0, "a" } };
            var text = Snapshot.Json(snap, pretty: false);
            var back = Json.ParseObject(text);
            Assert.Equal(12.5, back.D("sys_w"));
            Assert.Equal(80, back.I("bat_pct"));
            Assert.True(back.B("on_ac"));
            Assert.Equal("屏幕 \"x\"", back.S("name"));
            Assert.Equal(-1.0, back.D("nan"));
            Assert.Equal(2, Fx.Arr(back["list"]).Count);
        }
    }
}

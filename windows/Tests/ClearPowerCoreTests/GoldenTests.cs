// Golden tests: the Python daemon (daemon/clearpowerd) is the reference. Fixtures are
// produced by macos/scripts/gen-fixtures.py (shared with the Swift port) and compared
// value by value. Mirrors macos/Tests/ClearPowerCoreTests/GoldenTests.swift.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClearPower.Core;
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
        [Fact]
        public void BudgetDistribution()
        {
            var pb = new ProcessBudget(3, 600);
            pb.Sample(0, 4, () => new (string, double)[0]);
            var top = pb.Sample(10, 10, () => new[] { ("a", 50.0), ("b", 25.0), ("a", 25.0), ("c", 0.0), ("d", 5.0) });
            // floor is min over window = 4, budget 6 W; a=75 of 105
            Assert.Equal(3, top.Count);
            Assert.Equal("a", top[0].name); Fx.Near(top[0].w, 6 * 75.0 / 105.0, 1e-9);
            Assert.Equal("b", top[1].name); Assert.Equal("d", top[2].name);
            // Called again within interval -> cached
            Assert.Equal("a", pb.Sample(11, 20, () => new[] { ("z", 1.0) }).First().name);
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

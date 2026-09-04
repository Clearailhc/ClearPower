// Entry point: tray app by default; console modes for development and support.
//   ClearPower.exe                 tray application
//   ClearPower.exe --once [-v]     one snapshot as JSON (+ parts-vs-total check)
//   ClearPower.exe --charge        show the charge-control backend state
//   ClearPower.exe --quit          ask the running instance to exit
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ClearPower.Core;
using ClearPower.Win;

namespace ClearPower.App
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var a = args.ToList();
            if (a.Contains("--once")) return WithConsole(() => Once(a.Contains("-v")));
            if (a.Contains("--charge")) return WithConsole(() => ChargeInfo(a));
            if (a.Contains("--help") || a.Contains("-h")) return WithConsole(() => { Console.WriteLine(Usage); return 0; });
            if (a.Contains("--quit"))
            {
                try { System.Threading.EventWaitHandle.OpenExisting(App.QuitEventName).Set(); } catch (Exception) { }
                return 0;
            }
            var shot = a.IndexOf("--shot");
            if (shot >= 0) App.ShotPath = shot + 1 < a.Count ? a[shot + 1] : "clearpower-popover.png";
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }

        private const string Usage = "ClearPower.exe [--once [-v] | --charge [limit N|topup|cancel] | --shot file.png | --quit | --help]";

        /// <summary>A WinExe has no console; borrow the parent's so the output lands in the terminal.</summary>
        private static int WithConsole(Func<int> body)
        {
            var h = NativeMethodsApp.GetStdHandle(-11);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) NativeMethodsApp.AttachConsole(-1);
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            try { return body(); }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }

        private static int Once(bool verbose)
        {
            using var engine = new Engine(chargeHardware: ChargeBackends.Detect(verbose ? Console.Error.WriteLine : null));
            engine.Log = s => { if (verbose) Console.Error.WriteLine(s); };
            engine.Tick();
            Thread.Sleep(1200);
            var snap = engine.Tick();
            Console.WriteLine(Snapshot.Json(snap));
            var parts = new[] { "cpu_w", "gpu_w", "soc_w", "mem_w", "other_w" }.Select(k => snap.D(k)).Where(v => v >= 0).ToList();
            if (snap.D("display_w") >= 0) parts.Add(snap.D("display_w"));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "# sum(parts)={0:F3}  sys_w={1:F3}  source={2}", parts.Sum(), snap.D("sys_w"), snap.S("sys_source")));
            return 0;
        }

        /// <summary>--charge [limit N | topup | cancel]: inspect or drive the charge backend from a terminal.</summary>
        private static int ChargeInfo(List<string> a)
        {
            var hw = ChargeBackends.Detect(Console.WriteLine);
            Console.WriteLine($"backend: {hw.GetType().Name}, thresholds supported: {hw.ThresholdsSupported}, behaviours: {string.Join(",", hw.Behaviours)}");
            var sm = new ChargeStateMachine(hw);
            var i = a.IndexOf("--charge");
            var cmd = i + 1 < a.Count ? a[i + 1] : "";
            try
            {
                if (cmd == "limit" && i + 2 < a.Count) sm.SetLimit(int.Parse(a[i + 2], CultureInfo.InvariantCulture));
                else if (cmd == "topup") sm.StartTopUp();
                else if (cmd == "cancel") sm.Cancel();
            }
            catch (Exception e)
            {
                Console.WriteLine($"error: {e.Message}");
            }
            Console.WriteLine(Json.Serialize(sm.State, pretty: true));
            if (hw is IChargeHardwareInfo info)
            {
                info.Reassert();
                Console.WriteLine(Json.Serialize(info.ExtraState(), pretty: true));
            }
            (hw as IDisposable)?.Dispose();
            return 0;
        }
    }

    internal static class NativeMethodsApp
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);
    }
}

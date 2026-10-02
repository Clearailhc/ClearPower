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
            if (a.Contains("--procs")) return WithConsole(() => Procs(a.Contains("-v")));
            if (a.Contains("--charge-probe")) return WithConsole(ChargeProbe);
            if (a.Contains("--charge")) return WithConsole(() => ChargeInfo(a));
            if (a.Contains("--help") || a.Contains("-h")) return WithConsole(() => { Console.WriteLine(Usage); return 0; });
            if (a.Contains("--quit"))
            {
                try { System.Threading.EventWaitHandle.OpenExisting(App.QuitEventName).Set(); } catch (Exception) { }
                return 0;
            }
            var shot = a.IndexOf("--shot");
            if (shot >= 0) App.ShotPath = shot + 1 < a.Count ? a[shot + 1] : "clearpower-popover.png";
            var hover = a.IndexOf("--shot-hover");
            if (hover >= 0 && hover + 1 < a.Count) App.ShotHoverNode = a[hover + 1];
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }

        private const string Usage = "ClearPower.exe [--once [-v] | --procs [-v] | --charge-probe | --charge [limit N|topup|cancel|repair] [--dry-run] | --shot file.png [--shot-hover node] | --quit | --help]";

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

        /// <summary>
        /// --procs: what the apps box is working from, for support. Per-application power is a
        /// subtraction and a share, so when the box looks wrong the interesting numbers are the
        /// package power the attribution was given (a small -1 means not usable yet), the floor it
        /// is measured against, and the CPU share of each row.
        /// </summary>
        private static int Procs(bool verbose)
        {
            using var engine = new Engine(chargeHardware: ChargeBackends.Detect(verbose ? Console.Error.WriteLine : null));
            engine.Log = s => { if (verbose) Console.Error.WriteLine(s); };
            // RAPL needs a baseline, and the per-process source returns nothing until it has two
            // CPU-time readings to subtract from each other.
            for (int i = 0; i < 5; i++) { engine.Touch(); engine.Tick(); Thread.Sleep(1200); }
            engine.Touch();
            var snap = engine.Tick();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "package_w={0:F3}  cpu_w={1:F3}  sys_w={2:F3}  source={3}  rapl_available={4}",
                snap.D("package_w"), snap.D("cpu_w"), snap.D("sys_w"), snap.S("sys_source"), snap.B("rapl_available")));
            if (snap.D("cpu_w") < 0)
            {
                Console.WriteLine("# the apps box shows \"application power data unavailable\": there is no");
                Console.WriteLine("# per-block energy counter to attribute, so no app list can be produced.");
                return 0;
            }
            Console.WriteLine("# cpu_w is measured, so the apps box lists rows from:");
            var top = engine.GetTopProcesses(3);
            if (top.Count == 0)
            {
                Console.WriteLine("  (no row yet - run it again in a few seconds, or the machine is fully");
                Console.WriteLine("   idle so there is no dynamic CPU power to hand out)");
                return 0;
            }
            Console.WriteLine("  name                              watts     cpu%   shown");
            foreach (var (name, w, cpu) in top)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-30} {1,8:F3} {2,8:F1}   {3}", name, w, cpu, w >= 0.5));
            Console.WriteLine("# rows under 0.5 W are hidden, exactly as on Linux and macOS. For the first");
            Console.WriteLine("# seconds after startup, or after resume from sleep, every row is near 0 W: the");
            Console.WriteLine("# budget is the rise above the idle floor and nothing has risen yet.");
            return 0;
        }

        /// <summary>Which vendor providers the top-selling laptop brands are known to use.</summary>
        private static readonly (string Vendor, string Ns)[] VendorNamespaces =
        {
            ("Dell",   @"root\dcim\sysman"),
            ("Dell",   @"root\dcim\sysman\biosattributes"),
            ("Dell",   @"root\Dell"),
            ("HP",     @"root\HP\InstrumentedBIOS"),
            ("HP",     @"root\HP\BIOSSettingInterface"),
            ("HP",     @"root\HP"),
            ("ASUS",   @"root\Asus"),
            ("ASUS",   @"root\WMI"),
            ("Acer",   @"root\Acer"),
            ("MSI",    @"root\MSI"),
            ("Lenovo", @"root\Lenovo"),
            ("Lenovo", @"root\WMI"),
        };

        /// <summary>
        /// --charge-probe: what charge-control interfaces this machine exposes.
        ///
        /// Windows has no universal API for a charge limit, so support is per vendor and every
        /// vendor needs its own interface. This prints each backend's probe result, then for every
        /// known vendor provider the classes it offers that look like battery or charge control,
        /// with their properties and methods. That is exactly what is needed to name a vendor's
        /// charge setting instead of guessing at it.
        /// </summary>
        private static int ChargeProbe()
        {
            Console.WriteLine("== charge backends, in the order they are tried ==");
            var reports = ChargeBackends.Probe(s => Console.WriteLine($"   probe: {s}"));
            foreach (var r in reports)
            {
                Console.WriteLine($"  {r.State,-12} {r.Vendor,-8} {r.Name}");
                if (r.Detail.Length > 0) Console.WriteLine($"               {r.Detail}");
            }

            Console.WriteLine();
            Console.WriteLine("== which one would be used ==");
            var hw = ChargeBackends.Detect(s => Console.WriteLine("  " + s));
            Console.WriteLine($"  -> {hw.GetType().Name}: thresholds {hw.ThresholdsSupported}, behaviours [{string.Join(",", hw.Behaviours)}]");
            if (hw.LoadLimit() is int saved) Console.WriteLine($"  saved limit: {saved}%");
            if (hw is IChargeHardwareInfo info)
                foreach (var kv in info.ExtraState()) Console.WriteLine($"  {kv.Key} = {kv.Value}");

            Console.WriteLine();
            Console.WriteLine("== battery-related classes in root\\wmi ==");
            var battery = WmiProbe.ListClasses(@"root\wmi", "Battery");
            foreach (var c in battery) Console.WriteLine("  " + c);
            Console.WriteLine($"  ({battery.Count} classes; the standard ones are read-only and carry no charge limit)");

            Console.WriteLine();
            Console.WriteLine("== vendor providers ==");
            foreach (var ns in VendorNamespaces.Select(x => x.Ns).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var vendors = string.Join("/", VendorNamespaces.Where(x => x.Ns.Equals(ns, StringComparison.OrdinalIgnoreCase)).Select(x => x.Vendor).Distinct());
                var present = WmiProbe.NamespaceExists(ns);
                Console.WriteLine($"  {(present ? "present" : "absent ")}  {ns}   [{vendors}]");
                if (!present) continue;
                // Only the classes that could plausibly carry a charge setting, so the report stays
                // readable on a machine whose provider has hundreds of classes.
                foreach (var kw in new[] { "Battery", "Charge", "Power", "BIOS" })
                    foreach (var cls in WmiProbe.ListClasses(ns, kw))
                        DescribeClass(ns, cls, kw);
            }

            Console.WriteLine();
            Console.WriteLine("== root namespaces (to spot an unrecognised vendor provider) ==");
            foreach (var ns in WmiProbe.ListNamespaces()) Console.WriteLine("  " + ns);

            // A short, paste-ready block so a user on an uncovered machine does not have to decide
            // which part of the dump above matters.
            Console.WriteLine();
            Console.WriteLine("== paste this when asking for a vendor ==");
            Console.WriteLine("```");
            Console.WriteLine($"ClearPower {typeof(App).Assembly.GetName().Version?.ToString(3)} on {Environment.OSVersion.Version}");
            Console.WriteLine($"vendor: {MachineVendor()}  model: {MachineModel()}  cpu: {CpuName()}");
            Console.WriteLine($"x64: {Environment.Is64BitProcess}  logical cores: {Environment.ProcessorCount}");
            foreach (var r in reports) Console.WriteLine($"backend {r.Vendor}: {r.State} - {r.Detail}");
            Console.WriteLine($"chosen: {hw.GetType().Name} (thresholds {hw.ThresholdsSupported})");
            Console.WriteLine("```");
            Console.WriteLine("docs/charge-control.md says what it needs.");
            return 0;
        }

        /// <summary>Manufacturer and model from the firmware tables, for the probe report.</summary>
        private static string MachineVendor() => ReadFirmware("Manufacturer");
        private static string MachineModel() => ReadFirmware("ProductName", "SystemFamily");
        private static string CpuName() =>
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown";

        /// <summary>
        /// One property per query. Asking for several at once fails as a whole when the class does
        /// not carry them all (SystemFamily is absent on Windows 10), which is how the vendor and
        /// model came out as "unknown" on the machine this was written on.
        /// </summary>
        private static string ReadFirmware(string preferred, string? fallback = null)
        {
            foreach (var cls in new[] { "Win32_ComputerSystemProduct", "Win32_ComputerSystem", "Win32_BaseBoard" })
            {
                var v = QueryOne(cls, preferred) ?? (fallback == null ? null : QueryOne(cls, fallback));
                if (string.IsNullOrEmpty(v) || v == "System Product Name" || v == "To be filled by O.E.M.") continue;
                if (preferred == "Manufacturer") return v!;
                var m = QueryOne(cls, "Manufacturer");
                return string.IsNullOrEmpty(m) ? v! : $"{m} / {v}";
            }
            return "unknown";
        }

        private static string? QueryOne(string className, string property)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    new System.Management.ManagementScope(@"root\cimv2"),
                    new System.Management.ObjectQuery($"SELECT {property} FROM {className}"));
                foreach (System.Management.ManagementObject o in searcher.Get())
                    return o[property]?.ToString()?.Trim();
            }
            catch (Exception) { }
            return null;
        }

        private static readonly HashSet<string> Described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void DescribeClass(string ns, string cls, string keyword)
        {
            if (!Described.Add(ns + "\\" + cls)) return;   // the four keywords overlap
            var members = WmiProbe.DescribeClass(ns, cls);
            Console.WriteLine($"      {cls}  ({keyword})");
            if (members.Count == 0) { Console.WriteLine("        (no readable members)"); return; }
            foreach (var m in members.Take(24)) Console.WriteLine($"        {m}");
            if (members.Count > 24) Console.WriteLine($"        ... {members.Count - 24} more");
        }

        /// <summary>
        /// --charge [limit N | topup | cancel | repair] [--dry-run]: inspect or drive the charge
        /// backend from a terminal.
        ///
        /// With no command it only reads. That matters: this used to call Reassert() at the end
        /// unconditionally, so a read-only invocation still wrote the saved limit to the embedded
        /// controller. Writing now happens only for an explicit command, and `--dry-run` swaps the
        /// hardware for one that records the calls instead of making them.
        /// </summary>
        private static int ChargeInfo(List<string> a)
        {
            var dryRun = a.Contains("--dry-run");
            var real = ChargeBackends.Detect(Console.WriteLine);
            IChargeHardware hw = real;
            DryRunChargeHardware? recorded = null;
            if (dryRun)
            {
                recorded = new DryRunChargeHardware(real, Console.WriteLine);
                hw = recorded;
            }

            Console.WriteLine($"backend: {real.GetType().Name}, thresholds supported: {real.ThresholdsSupported}, behaviours: {string.Join(",", real.Behaviours)}");
            if (dryRun) Console.WriteLine("dry run: nothing will be written");
            var sm = new ChargeStateMachine(hw);
            var i = a.IndexOf("--charge");
            var cmd = i + 1 < a.Count ? a[i + 1] : "";
            if (cmd.StartsWith("--", StringComparison.Ordinal)) cmd = "";   // "--charge --dry-run"
            try
            {
                switch (cmd)
                {
                    case "": break;                                        // read-only
                    case "limit" when i + 2 < a.Count:
                        sm.SetLimit(int.Parse(a[i + 2], CultureInfo.InvariantCulture));
                        break;
                    case "limit":
                        Console.WriteLine("error: --charge limit needs a percentage");
                        break;
                    case "topup": sm.StartTopUp(); break;
                    case "cancel": sm.Cancel(); break;
                    // The app does this after a resume, because some firmware forgets the thresholds.
                    case "repair":
                        if (real is IChargeHardwareInfo info) info.Reassert();
                        else Console.WriteLine("this backend has no saved state to re-apply");
                        break;
                    default:
                        Console.WriteLine($"error: unknown command '{cmd}' (limit N | topup | cancel | repair)");
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"error: {e.Message}");
            }

            if (real is IChargeHardwareInfo realInfo)
                Console.WriteLine(Json.Serialize(realInfo.ExtraState(), pretty: true));
            Console.WriteLine(Json.Serialize(new Dictionary<string, object?>
            {
                ["charge_mode"] = sm.State["charge_mode"],
                ["charge_limit"] = sm.State["charge_limit"],
                ["charge_target"] = sm.State["charge_target"],
                ["saved_limit"] = real.LoadLimit() ?? (object?)null,
            }, pretty: true));
            if (recorded != null)
            {
                Console.WriteLine();
                Console.WriteLine(recorded.Entries.Count == 0
                    ? "dry run: no write was needed"
                    : $"dry run: {recorded.Entries.Count} call(s) would have been made");
            }
            (real as IDisposable)?.Dispose();
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



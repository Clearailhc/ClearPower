// The charge-control backends, tried in order until one reports that it can actually write.
//
// Windows has no universal interface for this. Microsoft does define a standard ACPI _DSM for a
// charge throttle (UUID 4c2067e3-887d-475c-9720-4af1d3ed602e, function 0x1, argument 0-100) and
// the control-method battery exposes _BTP, but neither is reachable from a normal user process
// through the battery class driver - the ACPI namespace belongs to the kernel. That is why every
// third-party tool ends up speaking a vendor interface instead.
//
// So each vendor is a small class implementing IVendorChargeBackend. Adding one does not touch the
// contract here: implement the two members, add it to All, and run `ClearPower.exe --charge-probe`
// on that machine to see what the probe found. See docs/charge-control.md.
//
// A backend must be safe on a machine that is not its vendor: probe by looking for the interface,
// return null when it is absent, and never assume a class or a method exists.
using System;
using System.Collections.Generic;
using ClearPower.Core;

namespace ClearPower.Win
{
    /// <summary>What a backend probe concluded, for the diagnostics trace.</summary>
    public enum BackendState
    {
        /// <summary>The vendor's interface exists and a control object was created.</summary>
        Ready,
        /// <summary>Not that vendor's machine, or the interface is absent.</summary>
        NotPresent,
        /// <summary>The vendor is recognised but the interface cannot be driven yet.</summary>
        Unavailable,
        /// <summary>Another backend already claimed the machine.</summary>
        Skipped,
    }

    public sealed class BackendReport
    {
        public string Name { get; }
        public string Vendor { get; }
        public BackendState State { get; internal set; }
        public string Detail { get; internal set; }

        public BackendReport(string name, string vendor)
        {
            Name = name;
            Vendor = vendor;
            Detail = "";
        }
    }

    public interface IVendorChargeBackend
    {
        string Name { get; }
        string Vendor { get; }

        /// <summary>
        /// Look for this vendor's interface and return hardware when it can be driven. Returns null
        /// when the machine is not this vendor's or the interface is missing.
        /// </summary>
        /// <param name="detail">Why the probe ended this way, for the trace.</param>
        IChargeHardware? Probe(Action<string> log, out string detail);
    }

    public static class ChargeBackends
    {
        private static readonly IVendorChargeBackend[] All =
        {
            new LenovoBackend(),
            new DellBackend(),
            new HpBackend(),
            new AsusBackend(),
            new MsiBackend(),
            new AcerBackend(),
        };

        /// <summary>Every backend with the outcome of its probe, for `--charge-probe`.</summary>
        public static List<BackendReport> Probe(Action<string>? log = null)
        {
            var sink = log ?? (_ => { });
            var reports = new List<BackendReport>();
            foreach (var b in All)
            {
                var r = new BackendReport(b.Name, b.Vendor);
                try
                {
                    var hw = b.Probe(sink, out var detail);
                    r.Detail = detail;
                    if (hw != null) r.State = BackendState.Ready;
                    // A backend that recognised its vendor but cannot drive it yet says so itself.
                    else r.State = detail.StartsWith("needs ", StringComparison.Ordinal)
                        ? BackendState.Unavailable
                        : BackendState.NotPresent;
                }
                catch (Exception e)
                {
                    r.State = BackendState.Unavailable;
                    r.Detail = e.Message;
                }
                reports.Add(r);
            }

            var claimed = false;
            foreach (var r in reports)
            {
                if (r.State != BackendState.Ready) continue;
                if (claimed) r.State = BackendState.Skipped;
                else claimed = true;
            }
            return reports;
        }

        /// <summary>The first backend that can drive this machine, or NullChargeHardware.</summary>
        public static IChargeHardware Detect(Action<string>? log)
        {
            var sink = log ?? (_ => { });
            foreach (var b in All)
            {
                try
                {
                    var hw = b.Probe(sink, out var detail);
                    if (hw != null)
                    {
                        sink($"charge control: {b.Name} ({detail})");
                        return hw;
                    }
                    if (detail.Length > 0) sink($"charge control: {b.Name}: {detail}");
                }
                catch (Exception e)
                {
                    sink($"charge control: {b.Name} failed: {e.Message}");
                }
            }
            sink("charge control: no supported vendor interface; charge controls stay hidden");
            return new NullChargeHardware();
        }
    }
}

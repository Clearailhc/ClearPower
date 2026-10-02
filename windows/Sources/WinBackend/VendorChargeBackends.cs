// The per-vendor charge backends.
//
// Only Lenovo is verified on real hardware. The others are here so the probe can report what a
// machine actually exposes and so adding the missing piece is a small, contained job - each one
// documents the interface it expects and what it would take to finish it. None of them write
// anything until their probe returns hardware, which only happens once the interface has been
// confirmed present, so on any other machine they are inert.
//
// The ACPI route is worth restating: the standard charge throttle is an ACPI _DSM that the kernel
// evaluates, and there is no documented user-mode IOCTL to reach it. A backend that wanted to use
// it would need either the vendor's own user-mode provider (what Lenovo and Dell ship) or a
// signed kernel driver. The latter is out of scope for this project: no driver, no elevation.
using System;
using ClearPower.Core;

namespace ClearPower.Win
{
    // ---- Lenovo -------------------------------------------------------------------------
    // Lenovo Power Manager's local RPC server (ncalrpc "BaseModuleRpcEndpoint_0"), the interface
    // Lenovo's own tools use. Reachable from a normal user session: no service, driver or prompt.
    // Documented by the MIT-licensed alandau/LenPwrCtl project.
    internal sealed class LenovoBackend : IVendorChargeBackend
    {
        public string Name => "Lenovo Power Manager RPC";
        public string Vendor => "Lenovo";

        public IChargeHardware? Probe(Action<string> log, out string detail)
        {
            var hw = LenovoPowerManager.TryCreate(log);
            if (hw == null)
            {
                detail = "RPC endpoint BaseModuleRpcEndpoint_0 not reachable";
                return null;
            }
            detail = "thresholds supported, verified on hardware";
            return hw;
        }
    }

    // ---- Dell ---------------------------------------------------------------------------
    // Dell ships two surfaces, depending on what is installed: Dell Command | Power Manager's WMI
    // provider under root\dcim\sysman, and the BIOS attribute provider under
    // root\dcim\sysman\biosattributes that Dell Command | Configure uses (the setting is named
    // PrimaryBatteryChargeConfiguration there). The class names differ between provider versions,
    // so this backend only reports what it can enumerate and never assumes a property exists.
    //
    // To finish it: on a Dell with Dell Command | Power Manager installed, run
    //   ClearPower.exe --charge-probe
    // which lists the classes that namespace actually exposes, then map the charge setting here.
    internal sealed class DellBackend : IVendorChargeBackend
    {
        public string Name => "Dell Command | Power Manager WMI";
        public string Vendor => "Dell";

        public IChargeHardware? Probe(Action<string> log, out string detail)
        {
            var sysman = WmiProbe.NamespaceExists(@"root\dcim\sysman");
            var bios = WmiProbe.NamespaceExists(@"root\dcim\sysman\biosattributes");
            if (!sysman && !bios)
            {
                detail = "no Dell WMI provider (Dell Command | Power Manager not installed)";
                return null;
            }
            detail = "needs the Dell charge setting mapped; provider present but not verified on hardware";
            return null;
        }
    }

    // ---- HP -----------------------------------------------------------------------------
    // HP laptops publish a battery charge limit through root\wmi. Implemented against the published
    // interface but not verified on HP hardware, so see the note at the top of this file.
    //
    // A provider that publishes the value without a setter is reported as read-only rather than
    // claimed, so the charge button stays hidden instead of failing when the user clicks it.
    internal sealed class HpBackend : IVendorChargeBackend
    {
        public string Name => "HP battery charge limit (root\\wmi)";
        public string Vendor => "HP";

        public IChargeHardware? Probe(Action<string> log, out string detail)
        {
            var limit = HpChargeLimit.Detect(log);
            if (limit != null)
            {
                if (!limit.Writable)
                {
                    detail = "charge-limit provider is read-only here; not claimed";
                    return null;
                }
                detail = "charge-limit provider found; unverified on HP hardware";
                return new WmiChargeHardware(limit);
            }
            if (WmiProbe.NamespaceExists(@"root\HP\InstrumentedBIOS"))
            {
                detail = "needs the HP BIOS setting mapped (Battery Health Manager); provider present, not verified";
                return null;
            }
            detail = "no HP charge-limit provider";
            return null;
        }
    }

    // ---- ASUS ---------------------------------------------------------------------------
    // ASUS laptops with the ATK interface expose battery health charging through
    // AsusAtkWMI_WMNB in root\wmi. The method numbering is public through the asus-wmi kernel
    // driver and its userspace tools, but it is reverse-engineered rather than documented by ASUS.
    internal sealed class AsusBackend : IVendorChargeBackend
    {
        public string Name => "ASUS ATK WMI (battery health charging)";
        public string Vendor => "ASUS";

        public IChargeHardware? Probe(Action<string> log, out string detail)
        {
            if (!WmiProbe.ClassExists("root\\wmi", "AsusAtkWMI_WMNB"))
            {
                detail = "no AsusAtkWMI_WMNB class";
                return null;
            }
            detail = "needs the ASUS ATK charge method mapped; interface present but not verified on hardware";
            return null;
        }
    }

    // ---- MSI ----------------------------------------------------------------------------
    // MSI exposes battery charge limits through its MSI_ACPI WMI class; the class and method names
    // move between generations.
    internal sealed class MsiBackend : IVendorChargeBackend
    {
        public string Name => "MSI ACPI WMI";
        public string Vendor => "MSI";

        public IChargeHardware? Probe(Action<string> log, out string detail)
        {
            if (!WmiProbe.ClassExists("root\\wmi", "MSI_ACPI"))
            {
                detail = "no MSI_ACPI class";
                return null;
            }
            detail = "needs the MSI charge method mapped; interface present but not verified on hardware";
            return null;
        }
    }

    // ---- Acer ---------------------------------------------------------------------------
    internal sealed class AcerBackend : IVendorChargeBackend
    {
        public string Name => "Acer WMI";
        public string Vendor => "Acer";

        public IChargeHardware? Probe(Action<string> log, out string detail)
        {
            var present = WmiProbe.ClassExists("root\\wmi", "AcerGamingWMI") ||
                          WmiProbe.ClassExists("root\\wmi", "AcerWMI") ||
                          WmiProbe.ClassExists("root\\wmi", "AcerBatteryCare") ||
                          WmiProbe.ClassExists("root\\wmi", "AcerDevice");
            if (!present) { detail = "no Acer WMI charge class"; return null; }
            detail = "needs the Acer charge method mapped; interface present but not verified on hardware";
            return null;
        }
    }
}

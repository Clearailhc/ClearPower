# Charge control on Windows

Windows has **no universal interface** for a battery charge limit, so ClearPower supports it per
vendor. This document explains what is possible, what is implemented, and how to add a machine
that is not covered.

## Why there is no universal route

Microsoft's hardware guidance does define a standard mechanism. The
[control-method battery](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/battery-and-charging)
implements an ACPI `_DSM` with UUID `4c2067e3-887d-475c-9720-4af1d3ed602e`:

| Function | Meaning |
|---|---|
| `0x1` | Set battery charge throttle, argument 0–100 (0 stops charging) |
| `0x2` | Whether the battery is user-serviceable |
| `0x3` | Charge watchdog interval the platform needs |

The same page specifies `_BTP` (battery trip point), which lets the firmware notify Windows when
the remaining capacity crosses a threshold.

Both are **evaluated by the kernel** on behalf of the OS power manager. There is no documented
user-mode IOCTL on the battery class driver that an application can call to reach them, and the
ACPI namespace is not exposed to user mode. A machine that implements the standard `_DSM` therefore
still cannot be driven by an ordinary application — which is why every third-party tool ends up
speaking some vendor interface instead.

Two consequences worth stating plainly:

- **A Windows API for this does not exist to be found.** Anyone claiming a universal user-mode
  charge-limit API is describing a vendor interface or a driver.
- **A kernel driver could do it**, by evaluating the `_DSM` itself. That would need a signed
  driver and an administrator install, and it is explicitly out of scope here: ClearPower installs
  no service, no driver and asks for no elevation.

The standard `root\wmi` battery classes (`BatteryStaticData`, `BatteryStatus`,
`BatteryFullChargedCapacity`, `BatteryCycleCount`, `BatteryTemperature`, …) are **read-only** and
carry no charge limit. They are useful for telemetry, not control.

## What is implemented

| Vendor | Interface | State |
|---|---|---|
| **Lenovo** | Power Manager local RPC (`ncalrpc` endpoint `BaseModuleRpcEndpoint_0`) | **Implemented and verified on hardware.** This is the interface Lenovo's own tools use, reachable from a normal user session; documented by the MIT-licensed [alandau/LenPwrCtl](https://github.com/alandau/LenPwrCtl) |
| **HP** | Charge limit in `root\wmi`: `HPBatteryChargeLimit` (`ChargeLimit` + a setter), or `HP_BIOSSettingInterface` | **Implemented against the published interface, not verified on HP hardware.** The provider publishes one value rather than a start/stop pair, so both thresholds carry it; a provider with no setter is reported read-only instead of claimed |
| Dell | Dell Command \| Power Manager WMI (`root\dcim\sysman`), or the BIOS attribute `PrimaryBatteryChargeConfiguration` under `root\dcim\sysman\biosattributes` | Probe detects the provider; the charge setting is **not mapped yet** |
| ASUS | `AsusAtkWMI_WMNB` in `root\wmi` (ATK interface, public via the `asus-wmi` kernel driver) | Probe detects the class; **not mapped yet** |
| MSI | `MSI_ACPI` in `root\wmi` | Probe detects the class; **not mapped yet** |
| Acer | `AcerGamingWMI` / `AcerWMI` / `AcerBatteryCare` | Probe detects the class; **not mapped yet** |

"Probe detects" means the backend checks whether the interface exists and reports it. A backend only
writes once it has been mapped, so on an unmapped machine the charge buttons stay hidden — the same
behaviour as unsupported Linux hardware. Being wired to a guess would be worse than being absent.

The HP path is worth reading as the worked example of the shape a provider must have: one readable
property plus a setter method. `WmiChargeHardware` adapts that to the two-threshold interface the
state machine expects (both thresholds carry the single value; the start threshold is the limit
minus the usual five-point hysteresis), and the equivalent for a new vendor is usually the same
shape with different names.

Vendors that expose **no** public interface at all are not listed: without a reverse-engineered
interface there is nothing to call. Direct ACPI EC access (WinRing0, `inpoutx64`) is deliberately
not used, because it needs a signed kernel driver and administrator rights.

## Adding a vendor

1. Run the probe on the machine and keep the output:

   ```powershell
   ClearPower.exe --charge-probe
   ```

   It prints each backend's result, which vendor namespaces exist, and which battery classes
   `root\wmi` exposes. That output is what the next step needs, and it is what to attach to an
   issue.

2. Add a class implementing `IVendorChargeBackend` in
   [`windows/Sources/WinBackend/VendorChargeBackends.cs`](../windows/Sources/WinBackend/VendorChargeBackends.cs)
   and list it in `ChargeBackends.All`. The contract is two members:

   ```csharp
   string Name { get; }
   string Vendor { get; }
   IChargeHardware? Probe(Action<string> log, out string detail);
   ```

   Return an `IChargeHardware` only when the interface is present **and** writable; return `null`
   with a short `detail` explaining why not. A backend must be inert on a machine that is not its
   vendor, so look the interface up (`WmiProbe.NamespaceExists` / `WmiProbe.ClassExists`) instead
   of assuming it.

3. The state machine handles the rest. `IChargeHardware.WriteThresholds(start, end)` is what a
   limit needs; `Behaviours` uses the Linux vocabulary (`"auto"`, `"inhibit-charge"`,
   `"force-discharge"`), and force-discharge is what makes the popover's Discharge button appear.
   `IChargeHardwareInfo` on the same object adds detail to the snapshot (`charge_start_threshold`,
   `control_method`, …).

4. Say in the table above whether it has been verified on real hardware. An unverified backend
   belongs in the list with that stated, not silently shipped as supported.

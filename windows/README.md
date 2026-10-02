# ClearPower for Windows (x64)

Tray app with the same popover as the Linux and macOS versions: charge limit / Top Up, a
power-flow diagram that adds up (adapter · battery → system → CPU · GPU · SoC · memory ·
display · other), runtime estimate, Windows power mode and the apps using significant
energy. English and Chinese.

Requires Windows 11 (the Energy Meter Interface that exposes RAPL) on an Intel laptop.
Windows 10 works with a single "system" node. Nothing to install besides the app: no
service, no driver, no administrator prompt; .NET Framework 4.8 is part of Windows.

## Install

1. Download `ClearPower-Setup-<version>-x64.exe` from [Releases](https://github.com/Clearailhc/ClearPower/releases)
   and run it (per-user install by default; SmartScreen warns because the binary is not
   code-signed — choose *More info › Run anyway*). Or unzip the portable
   `ClearPower-<version>-x64-portable.zip` anywhere and run `ClearPower.exe`.
2. The icon in the notification area shows the live system power. Click it for the popover,
   right-click for settings / quit. Windows may hide new icons in the overflow flyout; drag it
   onto the taskbar to keep it visible.
3. Optional: Settings › **Calibrate** once, **on battery** (about 90 s, the screen turns
   white), so the display gets its own number instead of being lumped into "other". On
   Windows the whole-machine total is only measurable from the battery; on AC it is
   estimated from SoC + memory + the calibrated baseline and shown with ≈.

## How it works on Windows

| Quantity | Source |
|---|---|
| System total | Battery discharge rate when on battery (physical truth, `IOCTL_BATTERY_QUERY_STATUS`); on AC an estimate (≈) |
| CPU / GPU / memory / SoC | Intel RAPL through the Windows 11 **Energy Meter Interface** (`\Energy Meter(RAPL_Package0_*)`): PP0, PP1, DRAM, PKG − PP0 − PP1 |
| Display | Calibration table × brightness (WMI `WmiMonitorBrightness`), optionally × screen content |
| Other | Total − everything above |
| Charge thresholds | **Lenovo Power Manager** local RPC (ThinkPad; the driver Windows Update installs). The EC keeps the thresholds across reboots and other operating systems. Other vendors: see below |
| Power mode | `PowerGetEffectiveOverlayScheme` / `PowerSetActiveOverlayScheme` (the Settings › Power mode slider) |
| Temperatures / fans | Not available without a kernel driver; hidden |

Two fields are easy to misread in `--once` output:

- `adapter_max_w` is **0 because Windows exposes no negotiated USB-PD contract and no adapter
  rating** through a public interface — not because no adapter is connected. `adapter_w` is the
  power actually drawn from the supply and is real; `on_ac` is the AC state.
- `psys_w` is always `-1` on Windows: there is no platform-power sensor. On AC the system total is
  the estimate described above and the UI marks it ≈.

Charge control needs a vendor interface. Windows has no universal API for a charge limit: the
standard mechanism is an ACPI `_DSM` that the kernel evaluates, with no user-mode route to it, so
every tool speaks a vendor interface instead. ClearPower keeps one backend per vendor and tries them
in order:

| Vendor | Charge limit |
|---|---|
| **Lenovo** | **Works.** Power Manager's local RPC, the interface Lenovo's own tools use; verified on hardware |
| **HP** | **Implemented, not verified on HP hardware.** A charge limit published in `root\wmi` |
| Dell, ASUS, MSI, Acer | The backend detects and reports the vendor's interface, but its charge setting is not mapped yet, so the buttons stay hidden rather than writing a guess |

`ClearPower.exe --charge-probe` prints exactly what a machine exposes — every backend's verdict, and
for a recognised vendor provider the classes it offers with their properties and methods. That
output is what adding a vendor needs; [docs/charge-control.md](../docs/charge-control.md) explains
the contract. Force-discharge has no public Windows interface, so the Discharge button stays hidden.

Notes on the estimates:

- **Apps.** Per-application power is the dynamic CPU power (package power − its ten-minute
  rolling *20th percentile*) split by CPU share, so the apps take the *rise* above idle rather
  than a slice of the whole machine; a row below 0.5 W is hidden, exactly as on Linux and macOS.
  The percentile rather than the minimum matters on Windows: a desktop always runs hundreds of
  processes and the CPU never reaches a true zero, so a minimum would sit well below what "idle
  plus the usual background" actually costs. While the package power is unknown the box says
  "application power data unavailable" instead of claiming nothing is running.
  `ClearPower.exe --procs` prints what the box is working from.
- **Screen content.** With `content-aware` on, the popover samples the screen every 5 s to scale
  the panel estimate by what is on it. That only answers on a single-monitor system, because the
  calibration measures one panel; with a second screen attached the reading is reported as
  unknown and the display stays folded into "other".
- **Monitors.** The popover anchors to the tray icon's own monitor at that monitor's scale, and
  re-places itself when the display configuration changes or the window's DPI scale does.

## Development

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e            # builds net48 with the .NET SDK
winget install --id JRSoftware.InnoSetup -e              # for the installer only
dotnet build windows/ClearPower.sln -c Debug
dotnet test  windows/Tests/ClearPowerCoreTests            # golden tests against the Python reference
ClearPower.exe --once -v                                  # one snapshot as JSON + parts-vs-total check
ClearPower.exe --charge [limit N | topup | cancel]        # inspect / drive the charge backend
ClearPower.exe --shot popover.png                         # render the popover and settings off-screen
ClearPower.exe --shot p.png --shot-hover cpu               # ... plus the hover detail card for a node
pwsh windows/build.ps1                                    # dist/ installer + portable zip + SHA256SUMS
```

Build outputs go to `%LOCALAPPDATA%\ClearPower-build` (the repository may live in a synced
folder). State lives in `%LOCALAPPDATA%\ClearPower` (`settings.json`, `state.json`,
`display_cal.json`, `clearpower.log`).

Layout:

```
Sources/ClearPowerCore   platform-independent logic, ported 1:1 from daemon/clearpowerd
                         (smoothing, runtime estimate, conserved breakdown, charge state
                         machine, display calibration, history, i18n) — same class names as
                         the Swift port
Sources/WinBackend       Energy Meter (PDH), battery IOCTLs, WMI brightness, process CPU,
                         power mode, Lenovo Power Manager RPC client, sampling engine
Sources/ClearPower       WPF tray app: tray icon, popover, Sankey, settings, calibration screen
Tests/                   golden tests; fixtures shared with macos/Tests (generated by the
                         Python daemon: macos/scripts/gen-fixtures.py)
installer/               Inno Setup script; scripts/make-icon.ps1 rasterises the app icon
```

The snapshot dictionary keeps the Linux key names and sentinels (`-1` = unknown, `bat_w`
positive into the battery); `sys_source` is `battery` on battery and `estimate` on AC.

The Lenovo interface is the MIT-licensed one documented by
[alandau/LenPwrCtl](https://github.com/alandau/LenPwrCtl); its MIDL-generated NDR format
strings are embedded (`WinBackend/Resources/lenpwr_*.bin`) and driven through rpcrt4's
`NdrClientCall2`, so no native stub is compiled or shipped.

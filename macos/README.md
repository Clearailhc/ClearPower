# ClearPower for macOS (Apple Silicon)

Menu bar app with the same popover as the Linux version: charge limit / Top Up / Discharge,
a power-flow diagram that adds up (adapter · battery → system → CPU · GPU · SoC · memory ·
display · other), runtime estimate, temperatures, fans, power modes and the apps using
significant energy. English and Chinese.

Requires macOS 14 or later on an Apple Silicon Mac.

## Install

1. Download `ClearPower-<version>-arm64.dmg` from [Releases](https://github.com/Clearailhc/ClearPower/releases),
   open it and drag **ClearPower** to **Applications**.
2. First launch: the app is not notarized (no Apple Developer ID yet), so macOS refuses to
   open it. Go to **System Settings › Privacy & Security**, scroll down and click
   **Open Anyway** next to the ClearPower message, then confirm. Alternatively, in Terminal:

   ```bash
   xattr -dr com.apple.quarantine /Applications/ClearPower.app
   ```

3. Click the ClearPower item in the menu bar and press **Install…** in the orange banner
   (or open Settings › Charging). macOS asks for an administrator password once: this
   installs a small privileged helper (`/Library/PrivilegedHelperTools/org.clearpower.helper`)
   that writes the SMC keys which stop and start charging. Everything else runs unprivileged.
4. Optional: Settings › **Launch at login**, and **Calibrate** the display once (about 45 s,
   the screen turns white) so the panel gets its own number instead of being lumped into "other".

If your menu bar is crowded, macOS may put the item into the "…" overflow; ⌘-drag it to a
better position.

**Quit AlDente / batt / other charge limiters** before using ClearPower — they fight over the same SMC keys.

## How it works on a Mac

| Quantity | Source |
|---|---|
| System total | SMC `PSTR` when on AC; the battery's own power (`PPBR`, `B0AC`) when discharging (physical truth) |
| CPU / GPU / memory | IOReport `Energy Model` counters: `CPU Energy`, `GPU Energy`, `DRAM` |
| SoC | ANE, media engines, memory controllers, display engines, PCIe (everything else on the die) |
| Display | Calibration table × brightness (DisplayServices), optionally × screen content |
| Other | Total − everything above (backlight before calibration, SSD, Wi-Fi, USB…) |
| Adapter | `AdapterDetails` from IOKit; DC-in power from SMC `PDTR` |
| Temperatures / fans | SMC `Tp*` (hottest CPU die sensor), `Tg*` (GPU), `TB0T` (battery), `F0Ac` |

None of this needs root. See [`scripts/probe/README.md`](scripts/probe/README.md) for the
hardware discovery notes.

**Charge control.** Apple Silicon has no firmware charge threshold on most machines, so the
helper enforces the limit itself: charging is inhibited at the limit and re-enabled 5 %
below it (the same start/end semantics as the Linux thresholds). Three key generations are
detected automatically: firmware limits (`bfF0/bfD0/bfE0`, macOS 27-era), `CH0B/CH0C` +
`CH0I`, and the Tahoe set `CHTE` + `CHIE`. Discharge cuts the adapter (`CHIE`/`CH0I`) until
the target is reached. Keys are re-asserted every cycle because a USB-PD renegotiation can
reset them. Before sleep, charging is stopped once the battery is inside the hysteresis
band, since nothing can enforce the limit while asleep; on wake the helper re-evaluates.
The helper restores charging on exit; special modes never survive a restart.

## Development

Only the Command Line Tools are needed (no Xcode project):

```bash
cd macos
swift build                                   # debug build
.build/debug/ClearPower --once -v             # one snapshot as JSON + parts-vs-total check
.build/debug/ClearPower --helper state        # talk to the installed helper
.build/debug/ClearPower --helper install      # (re)install the helper from this build
scripts/test.sh                               # golden tests against the Python reference
python3 scripts/gen-fixtures.py               # regenerate fixtures from daemon/clearpowerd
scripts/build-app.sh                          # dist/ClearPower.app (release, ad-hoc signed)
scripts/make-dmg.sh                           # dist/ClearPower-<version>-arm64.dmg
```

With a Developer ID: `SIGN_IDENTITY="Developer ID Application: …" scripts/build-app.sh` and
`NOTARIZE=1 scripts/make-dmg.sh` (after `xcrun notarytool store-credentials clearpower`).

Layout:

```
Sources/ClearPowerCore     platform-independent logic, ported 1:1 from daemon/clearpowerd
                           (smoothing, runtime estimate, conserved breakdown, charge state
                           machine, display calibration, history, i18n)
Sources/CSupport           C shims: AppleSMC user client, IOReport, DisplayServices
Sources/MacBackend         hardware sources + the sampling engine (runs inside the app)
Sources/ClearPowerIPC      XPC protocol app <-> helper
Sources/ClearPowerHelper   root launchd daemon: charge control only
Sources/ClearPowerApp      SwiftUI menu bar app (popover, Sankey, settings, calibration screen)
Tests/                     golden tests; fixtures generated by the Python daemon
scripts/probe              C probes used to discover SMC / IOReport channels
```

The snapshot dictionary keeps the Linux key names and sentinels (`-1` = unknown, `bat_w`
positive into the battery); `sys_source` is `smc` instead of `psys`.

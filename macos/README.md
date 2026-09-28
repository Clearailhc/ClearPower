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

**After updating from 0.5.x or earlier, reinstall the helper using the update banner or Settings → Charging.** The app and helper must both be 0.6.0 for the new charge controls.

**Quit AlDente / batt / other charge limiters** before using ClearPower — they fight over the same SMC keys.

## How it works on a Mac

| Quantity | Source |
|---|---|
| System total | Measured DC input minus signed battery power on AC (including conversion losses); battery current × voltage when discharging; `PSTR` as fallback |
| CPU / GPU / memory | IOReport `Energy Model`; M3 Max CPU rail fallback when PMGR counters stall; unavailable memory/SoC stays unknown |
| SoC | ANE, media engines, memory controllers, display engines, PCIe (everything else on the die) |
| Display | Live SMC backlight power on verified models (M1, M3 Max, M4, M5); calibration × brightness/content otherwise |
| Other | Total − everything above (backlight before calibration, SSD, Wi-Fi, USB…) |
| Adapter | Actual DC input from SMC `PDTR`, with IOKit `SystemPowerIn` fallback. `AdapterDetails.Watts` is the rated maximum, not consumption. |
| Temperatures / fans | SMC `Tp*` (hottest CPU die sensor), `Tg*` (GPU), `TB0T` (battery), `F0Ac` |

None of this needs root. See [`scripts/probe/README.md`](scripts/probe/README.md) for the
hardware discovery notes.

**macOS 27 compatibility.** Verified on MacBook Pro M3 Max (Mac15,10), macOS
27.0 build 26A428. Battery power uses signed `B0AC × B0AV`, not `PPBR`, which can
report an unrelated ~0.6 W while the battery is charging at ~35 W. New nested
`BatteryData` capacity fields restore battery health and runtime estimates.

The PMGR CPU/memory/SoC energy counters can stop updating on macOS 27. ClearPower
uses the independently measured CPU rails on M3 Max and retains the live AGX GPU
counter. CPU rail mappings are model-specific; other chips with stalled CPU counters
show unavailable data rather than a fabricated zero. Unavailable memory and SoC
power remains within “other”. Once stalled, delayed PMGR spikes are ignored until
restart. The total and backlight do not depend on these counters.

**Charge control.** Available interfaces are detected at runtime, since firmware
updates can also change older macOS releases. The original `CH0B/CH0C`, `CHTE`, and
`bfF0/bfD0/bfE0` backends remain supported when accessible. On macOS 27 firmware
that gates these keys, ClearPower uses the native PowerUI charge-limit interface.
Its choices come from macOS (80, 85, 90, 95, 100% on the tested Mac); arbitrary
50–100% limits remain available with the older SMC backends. Unsupported saved
limits produce an error and are not silently rounded or applied as 100%.

Native limits are managed by macOS during sleep too. macOS may occasionally charge
to 100% to maintain its state-of-charge estimate. Top Up temporarily sets 100%, then
restores the saved limit. Discharge uses the independently detected adapter switch
(`CHIE`/`CH0I`/`CH0J`); it does not require the old charging-enable keys. Failed writes
are reported, and cancelling discharge reconnects the adapter even if the saved limit
cannot be applied. On helper exit, the previous native limit is restored.

Legacy charge control still uses the helper's 5% hysteresis and pre-sleep inhibition.
Private interfaces may change in future macOS updates; unavailable control is shown
explicitly. No Apple-private entitlement or security-setting change is required.

## Development

Only the Command Line Tools are needed (no Xcode project):

```bash
cd macos
swift build                                   # debug build
"$(swift build --show-bin-path)/ClearPower" --once -v             # one snapshot as JSON + parts-vs-total check
.build/debug/ClearPower --helper state        # talk to the installed helper
.build/debug/ClearPower --helper install      # (re)install the helper from this build
scripts/test.sh                               # golden tests + macOS compatibility regression tests
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

`ClearPower --charge-capabilities` reports detected controls without changing them;
`ClearPower --loop 20 --json` records one JSON snapshot per second.

The snapshot dictionary keeps the Linux key names and sentinels (`-1` = unknown, `bat_w`
positive into the battery). `sys_source` is `dc-in`, `battery`, `smc`, or `estimate`.
`cpu_source`, `display_source`, `package_complete`, and `bat_power_available` expose
measurement provenance for diagnostics.

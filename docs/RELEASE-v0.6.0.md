# ClearPower 0.6.0 — macOS 27 compatibility

macOS 27 changed battery telemetry and restricted the older charging controls.
ClearPower now reads battery power from signed current × voltage, uses actual
adapter input for the power-flow diagram, and falls back to the native system
charge-limit interface. This fixes the case where a battery charging at about
35 W appeared as only 0.6 W and the adapter total was correspondingly too low.

- Battery capacity, health and runtime support the new nested `BatteryData` fields.
- Live backlight sensors are used on supported models; calibration remains the
  fallback elsewhere. Measured display power no longer carries the approximation mark.
- M3 Max CPU power uses verified SMC rails when PMGR energy counters stall. GPU
  readings remain live. Missing memory/SoC readings stay unknown, with that energy
  included in “other”; delayed counter updates cannot produce huge power spikes.
- Native charge limits are queried from macOS: 80, 85, 90, 95 and 100% on the tested
  Mac. Old SMC backends retain their existing range. Unsupported saved limits are
  reported instead of silently rounded. Native limits work during sleep; macOS may
  occasionally charge to 100% to maintain its battery estimate.
- Adapter discharge is detected separately from charging-enable controls. Failed
  operations are reported, and cancelling discharge restores adapter power.
- Application CPU enumeration, helper update notices, the diagnostic sampling loop,
  and packaging with the new Swift toolchain are corrected.

## Updating

Install the new app, then **reinstall the helper** from the update banner or
Settings → Charging. Both components must be updated for macOS 27 charging control.
macOS asks for an administrator password when replacing the helper. Quit other
charge limiters before using ClearPower's charging controls.

## Compatibility and validation

The macOS app still targets macOS 14+ on Apple Silicon. On-device validation uses
an M3 Max (Mac15,10), macOS 27.0 build 26A428. CPU fallback rail mappings are verified
for M3 Max only; on other chips with stalled counters, unavailable readings remain
unknown. Private interfaces may change with future firmware updates.

Local verification: 23 Swift tests (including legacy golden fixtures and macOS 27
regressions), 7 Linux tests, and a 16-snapshot live CPU workload trace with conserved
power flows. The native interface was verified by setting 95%, reading it back, then
restoring the previous 100% setting. Administrator/helper installation is separate
from the unprivileged telemetry and native-interface tests.

The release includes macOS, Linux and Windows packages. This update changes the
macOS implementation; Linux and Windows retain their existing behavior.

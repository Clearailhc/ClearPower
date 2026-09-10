A fan reading could outlive the sampling behind it. Fixed.

## Fixed

- **Fans no longer show a stale value.** Temperatures and fans are read only while the popover
  is open (that is what keeps the daemon idle the rest of the time), and the last reading was
  being served as if it were live: a fan that had stopped still showed its old rpm — e.g.
  `2200` — the next time the popover opened. The fan fields now read `-1` (unknown) whenever
  nothing is sampling, and the first sample after the popover opens reads immediately instead
  of waiting out the 3 s cache. Temperatures keep their last value while cold because
  `temp_cpu` is recorded in history. Linux and macOS; Windows has no fan sensor (it needs a
  kernel driver) and already reported `-1`.
- **A stopped fan is now visible as `0 rpm`** instead of vanishing from the temperature line,
  which made "stopped" and "unknown" look identical. The `-1` sentinel stays hidden, so a
  machine without a fan sensor shows no fan entry rather than a fake zero.

## Scope of this release

| | |
|---|---|
| **Platforms** | Linux (GNOME Shell 48–50), macOS 14+ (Apple Silicon), Windows 11 x64 |
| **Packages** | `clearpower_0.5.2_all.deb`, `ClearPower-0.5.2-arm64.dmg`, `ClearPower-Setup-0.5.2-x64.exe`, `ClearPower-0.5.2-x64-portable.zip` |
| **Unchanged** | The power breakdown, charge control, runtime estimate, display calibration and the `Snapshot` contract. Only the fan/temperature line in the open popover behaves differently. |

## Install

Same as 0.5.1 — see the [README](https://github.com/Clearailhc/ClearPower#install). On Linux:
`sudo apt install ./clearpower_0.5.2_all.deb`, then log out and back in once so the new
extension loads. On macOS / Windows the app updates in place; the Windows build is unsigned,
so SmartScreen still warns.

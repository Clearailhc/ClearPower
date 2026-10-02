# Changelog

All notable changes to ClearPower. Versions follow [SemVer](https://semver.org/).

## [0.7.0] — 2026-10-02

Windows-focused: the apps list, the popover's position and the node detail card were all wrong
there, and the same app-power bug existed on macOS and Linux.

### Fixed
- **Per-application power.** The idle floor was the raw ten-minute *minimum* of package power, so
  one bogus reading stuck for the whole window: an unknown package power (`-1` while RAPL or the
  Energy Meter is still being primed, or after resume from sleep) was recorded as 0 W and inflated
  every later budget, while a sample taken during the smoothing ramp was recorded as the floor and
  collapsed the budget to nothing — the apps box then claimed "no apps using significant energy"
  on a busy machine. The floor is now a low *percentile* of the window, which tracks the quiet end
  of the distribution instead of one outlier and cannot be moved by a load spike. Fixed on
  Windows, macOS (`ProcessBudget.swift`) and Linux (`procs.py`), which shared the bug verbatim.
- **Windows: the popover was anchored to a primary-monitor box that did not exist.**
  `SystemParameters.WorkArea` is always the primary monitor's usable area *in raw pixels*, so at
  225 % scale it reported 1280x752 while the real work area was 2880x1692 px = 1280x752 layout
  units, and the tray icon's physical rectangle was converted with whatever DPI the window
  happened to be on. The popover now resolves the monitor the icon is on, converts with *that*
  monitor's scale, clamps inside its usable area, and re-places itself when the display
  configuration or the window's DPI scale changes. `EnablePerMonitorDpiAwareness` is switched on
  — .NET Framework defaults it off, so WPF had been ignoring the PerMonitorV2 awareness the
  manifest always declared.
- **Windows: the node detail card never behaved like the other platforms.** It was a WPF
  `ToolTip` — a separate window placed at the pointer, which flicked out whenever the pointer
  touched it, was not clamped to the popover and kept the system's 250 ms delay. It is now drawn
  in the diagram itself, next to the node, as on macOS and GNOME.
- **Windows: the settings window** is constrained to its own monitor and re-fitted when display
  settings change, which also recovers a window stranded on a removed display.
- **Windows: screen-content sampling** no longer guesses which panel it is looking at. It only
  answers on a single-monitor system, because the calibration measures one panel; otherwise the
  reading is reported as unknown and the display stays folded into "other".

### Changed
- The apps poll no longer takes the engine lock from the UI thread (it could block on a sampling
  tick or a full process enumeration), and it no longer advances the energy sampling interval for
  nothing when power is unknown.
- The apps box distinguishes "application power data unavailable" (no per-block counter to
  attribute, so no list can be produced) from "no apps using significant energy", as macOS
  already did.

### Added
- `ClearPower.exe --procs` prints what the apps box is working from: the package power the
  attribution was given, the CPU power, and each row's watts and CPU share.
- `ClearPower.exe --shot --shot-hover <node>` renders the popover with a node's detail card, for
  off-screen visual checks.

### Validation
- Windows tests grew from 9 to 27: app-power attribution (unknown/ramping readings, interval
  handling, the percentile floor, a load spike) and window geometry (225 % primary, 100 % and
  150 % secondaries with negative origins, an oversized popover, recovery from a removed display).
- Verified on Windows 11 (16 logical cores, Energy Meter RAPL, 225 % scale): with one core fully
  busy the app appears in the list at 1.05 → 1.89 W as the load ramps, and the rows sum to the
  measured budget.
- **Not verified:** a real second display at a different scale was unavailable, so the
  cross-monitor paths are covered by unit tests and review only.

## [0.6.1] — 2026-09-28

- Refresh window layout and Canvas scale when displays or backing scales change;
  constrain popovers/settings to the current display's usable area and recover
  settings windows stranded on a removed display. Cancel calibration on reconfiguration.
- Read adapter-disable state directly from hardware. Label residual input readings
  explicitly, preserve actual fractional measurements, hide exactly zero input
  nodes, and reset stale smoothing when the observed power source changes.
- Draw input from its reported value rather than recomputing it in the UI; mark
  balance-based estimates. Compatible 0.6.0 helpers no longer prompt for reinstall.
- 34 Swift tests, including app graph and hosting-window layout regressions.

## [0.6.0] — 2026-09-28

### Fixed
- macOS 27 battery power now comes from signed current × voltage instead of the
  misleading PPBR sensor. New nested capacity fields restore health and runtime.
- Actual adapter input is used in the conserved power flow. Live backlight sensors
  replace the older calibration estimate on supported models.
- Stalled PMGR counters use verified CPU rails on M3 Max. Missing memory/SoC data
  remains unknown; delayed counter spikes do not become instantaneous power.
- macOS 27 charging uses native system limits when direct SMC control is gated.
  Discharge capability is detected independently. The UI exposes supported limits,
  helper upgrades and control failures instead of implying an inactive limit works.
- Process enumeration now scans the returned PID count; application power uses the
  CPU budget. Diagnostics retain their sampling timer for the whole recording.
- Packaging fails on build errors and resolves SwiftPM's actual output directory.
  Command Line Tools builds handle the macOS 27 State macro/toolchain changes.

### Validation
- macOS compatibility regression tests plus existing Python-reference golden tests.
- Hardware validation on M3 Max / macOS 27.0 (26A428); see the release notes for
  scope and limitations, including native limits starting at 80% on this Mac.

## [0.5.2] — 2026-09-11

### Fixed
- Fans are no longer republished from a stale cache. With the popover closed, Linux and macOS
  kept emitting the last fan reading, so a fan that had stopped still showed its old rpm (e.g.
  2200) the next time the popover opened — the temperature/fan block is refreshed on demand,
  and the cached copy was served as if it were live. The fan fields now read `-1` (unknown)
  whenever nothing is sampling, and the first sample after the popover opens reads immediately
  instead of waiting out the 3 s TTL. Temperatures keep their last value when cold because
  `temp_cpu` is recorded in history. Windows already reports both as `-1` (no sensor without a
  kernel driver) and now documents the rule.

### Changed
- The fan is shown whenever the reading is known, `0 rpm` included. A stopped fan used to
  vanish from the temperature line, which made "stopped" and "unknown" look identical.
  Unknown (`-1`: nothing is sampling, or no sensor — Windows) is still hidden.

## [0.5.1] — 2026-09-04

First release that actually ships packages for all three platforms: 0.5.0 was tagged but its
release build failed, so no `.deb`, DMG or installer was ever published. Same application as
0.5.0.

### Fixed
- Build: the macOS CI job ran on an Xcode without Swift Testing, so `import Testing` failed
  and the whole release was skipped; it now runs on macOS 15 with the newest Xcode on the
  image, and installs the Pillow the DMG background needs (a missing background no longer
  fails the build).
- macOS: `BatteryBarView` and `SankeyView` mixed `Double` and `CGFloat`, which Swift 6.2
  rejects as ambiguous (one expression also exceeded the type-checker's budget). The
  conversion now happens at the Canvas boundary.

## [0.5.0] — 2026-09-04

Intended as the first release for all three platforms at once (one GitHub Release with the
Linux `.deb`, the macOS `.dmg`, the Windows installer + portable zip and a single
`SHA256SUMS`, built by `.github/workflows/build.yml`) — the build failed, so this tag has no
packages; 0.5.1 ships them.

### Changed
- README rewritten as the project front page, with a Chinese version (`README.zh-CN.md`);
  Linux details moved to `docs/linux.md`.

### Fixed
- Windows: the popover flashed and closed on a tray click (a version-4 tray icon reports one
  click twice); the settings window could be taller than the screen (now scrolls); an energy
  sample taken within 200 ms of the previous one no longer drops the breakdown for a tick.

## [0.4.0] — 2026-09-04

### Added
- **Windows 11 (x64)**: tray app with the same popover, in `windows/` (C# / WPF on the
  in-box .NET Framework 4.8 — a single 200 KB `ClearPower.exe`, no runtime download, no
  service, no driver, no administrator prompt). RAPL comes from the Windows 11 Energy Meter
  Interface (`\Energy Meter(RAPL_Package0_*)` counters), the battery from the battery class
  driver IOCTLs, brightness from WMI, the power mode from the overlay-scheme API. Charge
  thresholds are written through Lenovo Power Manager's local RPC interface (ThinkPads),
  driven from C# via `NdrClientCall2` with embedded NDR format strings. Display calibration
  runs on battery (the only whole-machine sensor on Windows); on AC the total is estimated
  from SoC + memory + the calibrated baseline and marked ≈. Per-user Inno Setup installer,
  portable zip and a GitHub Actions workflow (`windows/build.ps1`).
- The Core golden tests now run in C# too, against the same fixtures as the Swift port.

### Fixed
- Linux: per-app energy attribution used the SoC remainder instead of the package power as
  its budget (`GetTopProcesses`).

## [0.3.0] — 2026-09-04

### Added
- **macOS (Apple Silicon)**: native menu bar app with the same popover, in `macos/`. Sampling
  runs unprivileged (IOKit battery, IOReport per-block energy counters, SMC temperatures /
  fans / platform power, DisplayServices brightness); a small root helper owns charge control
  through the SMC (`CHTE`/`CHIE`, `CH0B`/`CH0C`/`CH0I`, or firmware `bfF0` limits) with
  hysteresis, Top Up, Discharge, sleep handling and restore-on-exit. Distributed as an
  ad-hoc-signed DMG (`scripts/build-app.sh`, `scripts/make-dmg.sh`).
- Golden tests: the Python daemon generates fixtures (`macos/scripts/gen-fixtures.py`) that the
  Swift port must reproduce value for value.

### Fixed
- `bat_w` was zeroed while discharging: the signed battery power went through the `-1 =
  unknown` sentinel path of the smoother, so the system total never used the battery's own
  reading and the battery never appeared as a second source next to a weak adapter.

## [0.2.0] — 2026-09-04

### Added
- Conserved power breakdown: CPU · GPU · SoC · memory · display · other, derived from RAPL and psys so the parts always sum to the total; sinks under 0.1 W are folded into "other".
- Display power calibration (white screen + brightness sweep) and content-aware scaling from a low-resolution screen luminance sample; needed for OLED panels.
- Runtime / time-to-limit estimate from the battery energy counter over a 10 min / 30 min / 1 h window.
- Battery health line (full vs. design capacity, cycle count).
- Manual charge limit (50–100 %) in preferences; popover button cycles 80 / 90 / 100.
- English / Chinese UI, switchable at runtime.
- Cairo glyph icons for Sankey nodes; seamless band rendering; adapter and battery shown side by side when a weak adapter is assisted by the battery.
- `.deb` packaging, `clearpower` launcher/status CLI, autostart that enables the extension once per user.

### Changed
- All watt values pass a 5 s exponential smoother; one decimal everywhere.
- Top bar shows text only by default (icon optional).
- Daemon samples at 1 Hz only while someone is looking, 0.5 Hz otherwise; thermals and process scan on demand.

### Fixed
- Raising the charge limit failed with EINVAL (threshold write order).
- Display estimate no longer exceeds the measured remainder.

## [0.1.0] — 2026-09-04

Initial release: root daemon (sysfs / RAPL sampling, charge limit, Top Up, Discharge) and GNOME Shell popover with power-flow diagram.

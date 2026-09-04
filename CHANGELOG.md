# Changelog

All notable changes to ClearPower. Versions follow [SemVer](https://semver.org/).

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

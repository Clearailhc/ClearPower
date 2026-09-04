# Changelog

All notable changes to ClearPower. Versions follow [SemVer](https://semver.org/).

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

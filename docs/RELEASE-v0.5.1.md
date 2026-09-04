Battery charge control and an honest, live power-flow view for laptops — now on Windows too.

## Scope of this release

| | |
|---|---|
| **New platform** | Windows 11 x64 (Windows 10 runs with a single "system" node: no Energy Meter Interface) |
| **Package** | `ClearPower-Setup-0.5.1-x64.exe` (per-user installer, no admin), `ClearPower-0.5.1-x64-portable.zip` (single exe) |
| **Full feature set** | Intel laptops (RAPL via the Windows 11 Energy Meter Interface) that are ThinkPads with the Lenovo Power Manager driver (charge thresholds) |
| **Partial** | No Energy Meter → single "system" node, no display calibration · No Lenovo Power Manager → charge controls hidden · Discharge is never available on Windows · no temperatures / fans |
| **Tested on** | ThinkPad X14 Gen 1 (Core Ultra X7 358H, Samsung OLED), Windows 11 24H2 build 26200 |
| **Unchanged** | Linux `.deb` and macOS DMG from v0.3.0 (one Linux fix: per-app attribution budget) |

## Install (Windows)

Run the installer (SmartScreen will warn — the binary is not code-signed; choose *More info › Run anyway*) or unzip the portable build. Click the tray icon for the popover; Settings › **Calibrate** once **on battery** so the display gets its own number.

## Highlights

- Same popover, same numbers: the power breakdown is measured from the same RAPL domains as on Linux, and the battery is the whole-machine truth on battery. On AC the total is an estimate (≈) built from SoC + memory + the baseline learnt during calibration — Windows exposes no platform-power sensor.
- Charge limit 80/90/100 by click, or any value 50–100 in settings, written to the ThinkPad's embedded controller through Lenovo's own Power Manager interface — no driver, no elevation. Top Up runs to 100 % and reverts.
- Windows power mode (Best power efficiency / Balanced / Best performance) from the popover.
- English / Chinese UI, light / dark theme, per-monitor DPI.

## Also in 0.5.1

0.5.0 was tagged but never produced packages — its release build failed on macOS. 0.5.1 is
that same release, built: the macOS job now runs on a Swift 6 toolchain, and the SwiftUI
Canvas layout code no longer mixes `Double` with `CGFloat` in ways that toolchain rejects.
Nothing in the app's behaviour changed.

Full list in [CHANGELOG.md](https://github.com/Clearailhc/ClearPower/blob/main/CHANGELOG.md). Verify downloads with the attached `SHA256SUMS`.

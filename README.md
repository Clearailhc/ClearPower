<p align="center">
  <img src="icons/org.clearpower.ClearPower.svg" width="112" alt="ClearPower">
</p>
<h1 align="center">ClearPower</h1>
<p align="center">
  <strong>Battery charge limit and a live power breakdown for laptops.</strong><br>
  Linux · macOS · Windows &nbsp;—&nbsp; the same popover on all three.
</p>
<p align="center">
  <a href="https://github.com/Clearailhc/ClearPower/releases/latest"><img src="https://img.shields.io/github/v/release/Clearailhc/ClearPower?label=release&color=4FC386" alt="Release"></a>
  <a href="https://github.com/Clearailhc/ClearPower/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Clearailhc/ClearPower/build.yml?label=build" alt="Build"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue" alt="License"></a>
  <img src="https://img.shields.io/badge/platforms-Linux%20%7C%20macOS%20%7C%20Windows-6FB4F2" alt="Platforms">
</p>
<p align="center">
  <b>English</b> · <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <img src="docs/popover.png" width="330" alt="ClearPower on GNOME">&nbsp;&nbsp;
  <img src="docs/popover-windows.png" width="330" alt="ClearPower on Windows">
</p>

ClearPower stops charging at 80 % (or wherever you set the limit) and shows where every watt goes: adapter or battery → system → CPU · GPU · SoC · memory · display · other. Every number is either **measured by a sensor or derived by subtraction**, so the parts add up to the total. Built for laptops that stay plugged in; inspired by [AlDente](https://apphousekitchen.com/) on macOS.

## Features

- **Charge limit** — click to cycle 80 / 90 / 100 %, or pick any value 50–100 in settings. **Top Up** charges to 100 % once; **Discharge** drains down to the limit where the firmware allows it. Both restore the previous limit when they finish.
- **Power breakdown** — a live Sankey diagram fed by sensors: Intel RAPL or Apple's energy counters for the chip, the battery's own gauge for the whole machine. Nothing is modelled, and the parts add up to the total.
- **Display power, measured separately** — a one-time calibration (white screen and a brightness sweep) gives your panel its own curve, then scales it by what is actually on screen. On the same OLED, a full white page can cost 7 W and a dark interface 0.4 W.
- **Runtime estimate** from the battery's energy counter over a 10 min / 30 min / 1 h window, steadier than the OS estimate.
- **Power modes, temperatures, fans and the apps drawing noticeable power**, in the same popover.
- **Small footprint** — one small process; while the popover is closed it samples less often and stops drawing the diagram.
- English and Chinese UI, light and dark themes.

## Install

| Platform | Get it | Notes |
|---|---|---|
| **Windows 11** (x64) | [`ClearPower-Setup-<v>-x64.exe`](https://github.com/Clearailhc/ClearPower/releases/latest) or the portable zip | Per-user install, no admin, no runtime to download (a single 200 KB exe). Charge control on ThinkPads via the Lenovo driver. → [windows/README.md](windows/README.md) |
| **macOS** (Apple Silicon) | [`ClearPower-<v>-arm64.dmg`](https://github.com/Clearailhc/ClearPower/releases/latest) | macOS 14+. Charge control runs in a small privileged helper; installing it prompts for an admin password once. → [macos/README.md](macos/README.md) |
| **Linux** (GNOME 48–50) | [`clearpower_<v>_all.deb`](https://github.com/Clearailhc/ClearPower/releases/latest) or `./install.sh` | Root daemon + GNOME Shell extension. → [docs/linux.md](docs/linux.md) |

After installing, run **Settings › Calibrate** once so the display gets its own number instead of being folded into "other". On Windows, unplug first: the whole machine can only be measured on battery there.

Every release ships all three packages together, with a `SHA256SUMS` file.

## How the numbers are made

| Quantity | Linux | macOS | Windows |
|---|---|---|---|
| Whole machine | battery gauge on battery, RAPL `psys` on AC | battery gauge on battery, SMC `PSTR` on AC | battery gauge on battery; an estimate (≈) on AC |
| CPU / GPU / memory | RAPL `core` / `uncore` / `dram` | IOReport `CPU Energy` / `GPU Energy` / `DRAM` | Energy Meter `PP0` / `PP1` / `DRAM` |
| SoC (fabric, NPU, media…) | `package` − CPU − GPU | everything else on the die | `PKG` − PP0 − PP1 |
| Display | calibration curve × brightness × screen content | same | same |
| Other (SSD, Wi-Fi, USB…) | total − everything above | same | same |
| Charge thresholds | `charge_control_*_threshold` (sysfs) | SMC keys, written by the helper | Lenovo Power Manager (EC) |

All watt values pass a 5 s smoother before display. Per-platform details and hardware support tables live in the platform READMEs.

## Hardware support at a glance

| | Full breakdown | Charge limit | Discharge |
|---|---|---|---|
| Linux | Intel RAPL | ThinkPad and other vendors with the kernel threshold interface | ThinkPad |
| macOS | Apple Silicon | all Apple Silicon Macs | yes |
| Windows | Intel (Windows 11 Energy Meter Interface) | ThinkPad (Lenovo Power Manager driver) | – |

AMD RAPL, other vendors' charge interfaces and Windows sensor drivers are not done yet; PRs welcome — see [Contributing](#contributing).

## Repository layout

```
daemon/          Linux backend (Python): the reference implementation of the Snapshot contract
extension/       GNOME Shell frontend
macos/           Swift package: core logic, IOKit/SMC backend, privileged helper, SwiftUI app
windows/         C#/WPF: core logic, Energy Meter / battery / Lenovo backend, tray app, installer
docs/            per-platform notes, release notes, screenshots
packaging/       systemd unit, D-Bus policy, polkit action, desktop entries, deb scripts
```

All three ports produce the same `Snapshot` dictionary (same keys, same units, `-1` = unknown) and are checked value by value against the Python daemon with golden tests (`macos/scripts/gen-fixtures.py`, fixtures shared by the Swift and C# ports). A new platform only needs a backend that fills that dictionary.

## Contributing

Issues and pull requests are welcome: RAPL on AMD, charge-threshold interfaces of other vendors (Linux and Windows), translations (one dictionary per platform, same keys), and frontends for other desktops. Two rules:

1. every number shown is measured or derived from measurements — no modelling, and anything estimated is marked ≈;
2. no extra sampling while the popover is closed.

Roadmap: Windows charge control beyond Lenovo and a signed sensor driver for temperatures; macOS notarization and SMAppService; AMD RAPL on Linux.

## License

[Apache-2.0](LICENSE)

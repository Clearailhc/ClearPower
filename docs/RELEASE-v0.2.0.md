Battery charge control and an honest, live power-flow view for Linux laptops — inspired by AlDente.

## Scope of this release

| | |
|---|---|
| **Platform** | Linux with GNOME Shell **48, 49 or 50** (Wayland or X11). Ubuntu 24.10+ / 26.04, Fedora 42+, Arch |
| **Package** | `clearpower_0.2.0_all.deb` for Debian/Ubuntu; other distros: `./install.sh` from source |
| **Full feature set** | Intel laptops with RAPL (`intel-rapl` powercap) and a ThinkPad-style battery interface (`charge_control_*_threshold`, `charge_behaviour`) |
| **Partial** | No RAPL → single "system" node, no display calibration · No threshold interface → charge controls hidden · No `force-discharge` → Discharge hidden |
| **Tested on** | ThinkPad X14 Gen 1 (Core Ultra X7 358H, Samsung ATNA40HQ10 OLED), Ubuntu 26.04.1, GNOME Shell 50.1 |
| **Not yet** | AMD RAPL, Windows, macOS — **Windows is next** |

## Install

```bash
sudo apt install ./clearpower_0.2.0_all.deb
```

Log out and back in once, then open **ClearPower** (app grid) → **Calibrate** so the display gets its own measured number.

## Highlights

- Power breakdown that always adds up: CPU · GPU · SoC · memory · display · other.
- Display power measured on your own panel (white-screen brightness sweep) and scaled by live screen content — on this OLED, full white at 100 % is 7.6 W, a dark desktop 0.4 W.
- Runtime estimate from the battery's energy counter over a selectable window.
- Charge limit 80/90/100 by click, or any value 50–100 in settings; Top Up and Discharge with automatic revert.
- English / Chinese UI.

Full list in [CHANGELOG.md](https://github.com/Clearailhc/ClearPower/blob/main/CHANGELOG.md). Verify the download with the attached `SHA256SUMS`.

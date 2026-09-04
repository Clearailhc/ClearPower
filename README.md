# ClearPower

<img src="icons/org.clearpower.ClearPower.svg" width="96" align="right" alt="ClearPower icon">

AlDente-style battery charge control and live power-flow monitor for Linux
laptops. Tested on a ThinkPad X14 Gen 1 with Ubuntu 26.04 / GNOME Shell 50.

* **Top-bar indicator** with live system power draw. The popover has the
  charge-limit slider, Discharge / Top Up, a battery bar, an animated power-flow
  diagram (adapter/battery → system → SoC / display / other), the power-profile
  switcher, temperatures and fans, and the apps using significant energy.
* **`clearpowerd`**, a small root daemon (systemd + D-Bus + polkit) that
  samples sysfs and Intel RAPL at 1 Hz and owns the charge-control state
  machine, so limits, top-ups and discharges keep working while the popover is
  closed. Idle cost is a fraction of a percent of one core; the UI animates only
  while the popover is open and respects the system "reduce animations" setting.

## Install (Debian / Ubuntu)

Download `clearpower_<version>_all.deb` from the releases page, or build it from
this checkout, then:

```bash
sudo apt install ./dist/clearpower_0.1.0_all.deb
```

Log out and back in once. The indicator is enabled automatically at login; the
**ClearPower** entry in the app grid (or `clearpower` in a terminal) enables it
on demand and opens the settings. `clearpower status` shows daemon and
extension state.

From a checkout, `./install.sh` builds the package and installs it (it also
removes the pre-package manual layout if you had one). `./uninstall.sh` removes it.

## Repository layout

```
daemon/clearpowerd/      Linux backend: sampling + charge control (python3, PyGObject, psutil)
daemon/data/             D-Bus introspection XML — the frontend/backend contract
extension/clearpower@lhc GNOME Shell frontend (ESM): indicator, popover, Sankey, prefs
bin/clearpower           launcher / helper CLI
icons/                   app icon (scalable) and symbolic top-bar icon
packaging/               systemd unit, D-Bus policy, polkit action, desktop entries, deb scripts
packaging/deb/build.sh   builds dist/clearpower_<version>_all.deb with plain dpkg-deb
```

## Architecture and portability

ClearPower is split so that other platforms can be added without touching the UI logic:

1. **Backend** (`daemon/`): reads hardware and writes charge control. Linux-only
   today (sysfs, RAPL, `charge_behaviour`).
2. **Contract** (`daemon/data/org.clearpower.Daemon1.xml`): a flat `Snapshot`
   dictionary (`sys_w`, `soc_w`, `display_w`, `other_w`, `bat_w`, `bat_pct`,
   `on_ac`, temperatures, …) plus four control calls (`SetChargeLimit`,
   `StartTopUp`, `StartDischarge`, `CancelSpecial`). Everything the popover draws
   comes from this dictionary.
3. **Frontend** (`extension/`): GNOME Shell today. A macOS menu-bar app or a
   Windows tray app would implement the same snapshot from SMC / WMI + battery
   APIs and reuse the layout, palette and Sankey rules documented in
   `extension/clearpower@lhc/sankey.js`.

## D-Bus API

`org.clearpower.Daemon1` at `/org/clearpower/Daemon` on the system bus.
Properties `Snapshot`, `ChargeMode`, `ChargeLimit`, `ChargeTarget`,
`ChargeControlSupported`, `DischargeSupported`; methods `SetChargeLimit(i)`,
`StartTopUp()`, `StartDischarge(i)`, `CancelSpecial()`, `GetTopProcesses(i)`,
`GetHistory(s,i)`; signal `Sample(a{sv})` at 1 Hz.

```bash
busctl --system get-property org.clearpower.Daemon1 /org/clearpower/Daemon org.clearpower.Daemon1 Snapshot
busctl --system call org.clearpower.Daemon1 /org/clearpower/Daemon org.clearpower.Daemon1 SetChargeLimit i 80
journalctl -u clearpowerd -f
```

## Charge control semantics

* **Limit L** (50–100 %, step 5): `charge_control_end_threshold = L`,
  `start = L-5`, `charge_behaviour = auto`.
* **Top Up**: thresholds temporarily 95/100 until the battery reports Full,
  then the limit is restored.
* **Discharge**: `charge_behaviour = force-discharge` until the battery reaches
  the limit (never below 20 %), then `auto`.
* Special modes never survive a daemon restart; SIGTERM restores `auto`.

## Development

Run the daemon unprivileged on the session bus:

```bash
cd daemon && python3 -m clearpowerd --bus session -v
```

With `CLEARPOWER_BUS=session CLEARPOWER_DEV=1` in GNOME Shell's environment the
extension connects to the session bus, opens its menu on start and enables
unsafe mode so `org.gnome.Shell.Eval` and screenshots work — handy together with
`gnome-shell --headless --wayland --virtual-monitor 1280x800` under
`dbus-run-session`.

Optional daemon config: `/etc/clearpower/config.json` (keys and defaults in
`daemon/clearpowerd/config.py`, e.g. `display_p_min_w` / `display_p_max_w`).

## License

Apache-2.0.

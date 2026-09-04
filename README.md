# ClearPower

AlDente-style power monitor and battery charge control for Linux laptops
(GNOME Shell 48–50, tested on a ThinkPad X14 Gen 1 / Ubuntu 26.04).

* Top-bar indicator with live system power draw and a popover:
  charge-limit slider, Discharge / Top Up, battery bar, power-flow diagram
  (adapter/battery → system → SoC/display/other), power-profile switcher,
  temperatures and fans, apps using significant energy.
* `clearpowerd`: a small Python daemon running as root (systemd + D-Bus +
  polkit) that samples sysfs/RAPL at 1 Hz and owns the charge-control state
  machine, so limits, top-ups and discharges keep working when the UI is closed.

## Install

```bash
./install.sh
```

Copies the daemon to `/usr/local/lib/clearpower`, installs the systemd unit,
D-Bus policy and polkit action, then installs and enables the extension for the
current user. On Wayland, log out and back in once so GNOME Shell loads the
extension.

## Layout

```
daemon/clearpowerd/      daemon package (python3 + PyGObject + psutil)
daemon/data/             D-Bus introspection XML
packaging/               clearpowerd.service, D-Bus conf, polkit policy
extension/clearpower@lhc GNOME Shell extension (ESM)
```

## D-Bus API

`org.clearpower.Daemon1` at `/org/clearpower/Daemon` on the system bus:
properties `Snapshot`, `ChargeMode`, `ChargeLimit`, `ChargeTarget`;
methods `SetChargeLimit(i)`, `StartTopUp()`, `StartDischarge(i)`,
`CancelSpecial()`, `GetTopProcesses(i)`, `GetHistory(s,i)`; signal `Sample(a{sv})`.

```bash
busctl --system get-property org.clearpower.Daemon1 /org/clearpower/Daemon org.clearpower.Daemon1 Snapshot
busctl --system call org.clearpower.Daemon1 /org/clearpower/Daemon org.clearpower.Daemon1 SetChargeLimit i 80
journalctl -u clearpowerd -f
```

## Charge control semantics

* **Limit L** (50–100, step 5): `charge_control_end_threshold = L`,
  `start = L-5`, `charge_behaviour = auto`.
* **Top Up**: thresholds temporarily 95/100 until the battery reports Full,
  then the limit is restored.
* **Discharge**: `charge_behaviour = force-discharge` until the battery reaches
  the limit (never below 20 %), then `auto`.
* Special modes never survive a daemon restart; SIGTERM restores `auto`.

## Development

Run the daemon unprivileged on the session bus and a throwaway headless shell:

```bash
cd daemon && python3 -m clearpowerd --bus session -v
```

Set `CLEARPOWER_BUS=session CLEARPOWER_DEV=1` in the shell's environment; the
extension then connects to the session bus, opens its menu on start and enables
unsafe mode so `org.gnome.Shell.Eval` and screenshots work.

Optional daemon config: `/etc/clearpower/config.json` (keys and defaults in
`daemon/clearpowerd/config.py`, e.g. `display_p_min_w` / `display_p_max_w`).

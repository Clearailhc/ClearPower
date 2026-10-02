# ClearPower 0.7.0 — Windows apps list, popover placement and node details

Three things were visibly wrong on Windows, and the app list was wrong on every platform.

## The apps list

Per-application power is the machine's *dynamic* CPU power shared out by CPU share, so it needs a
baseline: the package power the machine draws when nothing is running. That baseline was the raw
ten-minute **minimum**, and a minimum is pinned by whichever single reading happened to be lowest.
Two ways that broke:

- The first RAPL/Energy Meter read has no delta yet, so `package_w` is `-1`. Recorded as 0 W it
  became the baseline, which made every later budget the *full* package power for ten minutes.
- The package power is smoothed. On startup, and again after wake from sleep, it ramps up from
  that first value, so the samples taken while it converges sit far below the real idle draw.
  Pinning one of those left a budget of about zero — and the box then said "no apps using
  significant energy" while the machine was busy.

The baseline is now a low **percentile** of the window rather than its minimum. It tracks the quiet
end of the distribution instead of one outlier, and twenty seconds of sustained full load cannot
move it. On a desktop this matters more than it sounds: Windows always runs hundreds of processes
and the CPU never reaches a true zero, so the minimum of the window sits well below what "idle plus
the usual background" actually costs.

The box also now distinguishes **"application power data unavailable"** — no per-block energy
counter to attribute, so no list can be produced — from **"no apps using significant energy"**.
Previously both looked identical, which made a broken reading look like an idle machine.

Linux (`daemon/clearpowerd/sources/procs.py`) and macOS
(`macos/Sources/ClearPowerCore/ProcessBudget.swift`) carried the identical baseline bug and are
fixed the same way, so all three platforms stay one algorithm.

## Popover placement on Windows

`SystemParameters.WorkArea` is always the **primary** monitor's usable area, and it is in raw
pixels while a WPF window lays out in units scaled by the DPI of whichever monitor it is on. On the
225 % display used to verify this it reported `1280x752` while the real work area was
`2880x1692` pixels = `1280x752` layout units — so the box the popover was clamped against was
2.25x too small in each direction, and on a secondary display it was simply the wrong screen. The
tray icon's physical rectangle was then converted with whatever DPI the popover was *already* on,
which put the window in the wrong place whenever the icon was on a different display.

The popover now:

- resolves the monitor the anchor is actually on (`MonitorFromPoint` / `MonitorFromWindow`),
- converts the anchor with **that** monitor's scale (`GetDpiForMonitor`),
- clamps inside **that** monitor's usable area (`GetMonitorInfo`),
- re-places itself when the display configuration changes (a debounced `WM_DISPLAYCHANGE`) or when
  the window's own DPI scale changes, and
- positions itself again once `SizeToContent` has settled, which it had not when the old code read
  the height.

`.NET Framework` defaults `EnablePerMonitorDpiAwareness` to **off**, so WPF had been ignoring the
PerMonitorV2 awareness the manifest has always declared; that is now switched on.

The settings window is constrained to its own monitor and re-fitted when display settings change,
which also recovers a window stranded on a display that has been unplugged.

## Node detail card

Hovering a flow node showed a WPF `ToolTip`: a separate top-level window placed at the pointer.
Moving onto it fired `MouseLeave` on the diagram, which closed it, which put the pointer back on
the node — a visible flicker; it was also not clamped to the popover and kept the system's 250 ms
delay. It is now drawn inside the diagram next to the node, above it or below it depending on
room, exactly as the macOS and GNOME frontends do.

## Also

- `ClearPower.exe --procs` prints what the apps box is working from: the package power the
  attribution was given, the CPU power, and each row's watts and CPU share. Useful when a number
  looks wrong.
- `ClearPower.exe --shot p.png --shot-hover cpu` renders the popover with a node's detail card.
- The apps poll no longer takes the engine lock from the UI thread, so it cannot stall the popover
  behind a sampling tick.
- Screen-content sampling only answers on a single-monitor system, because the calibration measures
  one panel; otherwise the display stays folded into "other" instead of being scaled by the wrong
  screen.

## Scope and validation

| | |
|---|---|
| **Changed** | Windows popover placement, node details, apps list, settings window · apps list on all three platforms |
| **Unchanged** | Charge control (Windows ThinkPad / macOS helper / Linux sysfs), display calibration, runtime estimate |
| **Tested on** | Windows 11 (16 logical cores, Energy Meter RAPL available, 225 % scale); the macOS and Linux changes are the same arithmetic and are covered by the shared golden tests |
| **Verification** | Windows tests 9 → 27 (app-power attribution and per-monitor geometry); with one core fully busy the application appears in the list at 1.05 → 1.89 W as the load ramps, and the rows sum to the measured budget |
| **Not verified** | a real second display at a different scale was not available, so the cross-monitor paths are covered by unit tests and code review only |

## Install

- **Windows** — `ClearPower-Setup-0.7.0-x64.exe` (per-user, no admin prompt; SmartScreen warns
  because the binary is not code-signed, choose *More info › Run anyway*) or the portable
  `ClearPower-0.7.0-x64-portable.zip`.
- **macOS** — the DMG. The 0.6.0 charging helper remains compatible, so no helper update is needed.
- **Linux** — `clearpower_0.7.0_all.deb`.

Verify downloads with the attached `SHA256SUMS`. Full list in
[CHANGELOG.md](https://github.com/Clearailhc/ClearPower/blob/main/CHANGELOG.md).

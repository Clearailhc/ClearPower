# ClearPower 0.6.1 — display changes and input-power clarity

Connecting, disconnecting, or changing the scale of a display now refreshes the
popover's layout and Canvas scale using its actual hosting screen. Layout stays in
logical points. Tall popovers scroll on smaller screens; settings windows are
constrained to the available screen and recovered when their display disappears.
Display calibration is cancelled if the display configuration changes, and its
white surface targets the built-in display.

The adapter's hardware disable switch is now read on every sample, independently
of the helper's cached state. When disabled, the UI labels nonzero input telemetry
as an **input sensor reading**, with an explanation that it may include standby
draw or sensor offset rather than useful supply. Actual 0.x W readings are retained;
exactly zero input nodes are hidden. Values below 0.05 W display as `<0.1 W` instead
of rounding a nonzero measurement to `0.0 W`. Source changes reset stale smoothing.

On the tested M3 Max running macOS 27, CHIE read back `08` (adapter disabled), while
PDTR reported approximately 0.448 W and IOKit approximately 0.444 W (20 V, 22 mA).
AlDente hid its adapter node in the same discharge state. This difference in display
does not establish physical zero input; an external meter would be required to
distinguish standby consumption from sensor offset. The previous ClearPower 0.3.0
process was also still running after the on-disk app had been updated.

Validation: 34 Swift tests, including power-source transitions, legacy fixtures,
zero/fractional input graphs, small-screen and negative-origin geometry, and real
AppKit/SwiftUI hosting-window refreshes. Physical monitor unplug/replug testing has
not been completed. The optimized app builds and passes ad-hoc signature checks.

## Updating

Quit the running ClearPower process and launch the updated app. Replacing the file
alone does not update an already-running process. Check the version in Settings.
The **0.6.0 helper remains compatible**, so this patch does not require reinstalling
it. Older helpers still need an update.

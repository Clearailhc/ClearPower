# ClearPower 0.7.1 — easier to adopt on a machine that is not covered yet

A follow-up to 0.7.0. Nothing here changes how power is measured; it makes the charge-control work
easier to finish for a laptop that is not supported yet, and makes a bad display calibration say so.

## Windows: charge control

Lenovo is verified on hardware. HP is implemented against its published `root\wmi` interface, on the
same adapter, but **not verified on HP hardware**. Dell, ASUS, MSI and Acer detect their own
interface and report it; their charge setting is not mapped, so the buttons stay hidden rather than
writing a guess.

There is no universal route to a charge limit on Windows, which is worth stating plainly because it
is the reason support is per vendor: Microsoft does define a standard ACPI `_DSM` for a charge
throttle, and the control-method battery exposes `_BTP`, but both are evaluated by the kernel and
there is no documented user-mode way to reach them. The standard `root\wmi` battery classes are
read-only. The only remaining universal option is a signed kernel driver, which this project does
not ship — no service, no driver, no elevation. [docs/charge-control.md](charge-control.md) has the
detail, the vendor table and the contract for adding one.

Three new things make that practical:

- **`ClearPower.exe --charge-probe`** prints what the machine exposes: every backend's verdict, which
  vendor namespaces exist, and for the classes that look like battery or charge control their
  properties and methods. It ends with a short paste-ready block — version, OS, machine vendor and
  model, CPU, backends — so reporting an uncovered laptop is one step rather than a judgement call
  about which part of the dump matters.
- **`ClearPower.exe --charge --dry-run`** prints the exact calls a command would make (thresholds,
  behaviour, saved limit) and makes none of them. Writing that wrapper also fixed a real bug: a plain
  read-only `--charge` used to write the saved limit to the embedded controller anyway.
- **`ClearPower.exe --charge repair`** re-applies the saved limit. The app already did that after a
  resume, because some firmware forgets the thresholds; now it can be done without restarting.

## Windows: a bad calibration now says so

The panel table is built from a running maximum of (battery − SoC − memory) at each brightness level,
so a sweep taken while the machine was busy flattens at the top. The machine this was developed on
produced `(50, 1.302) (75, 1.302) (100, 1.302)`: a "calibrated" display whose brightness does
nothing, with nothing in the UI to suggest that re-running on a quiet machine would fix it.

Three or more collapsed top levels — or a sweep screen that did not measure as white — now sets a
message in Settings saying the calibration looks unreliable and to keep the machine idle, then
calibrate again.

## Also

- The apps box reuses its rows instead of rebuilding them every three seconds, so the layout no
  longer re-runs for nothing while the popover is open.
- The README's hardware support table now matches `docs/charge-control.md` (it still said ThinkPad
  only for Windows charge limits), and `adapter_max_w = 0` is documented as "Windows exposes no
  adapter rating", not "no adapter connected".

## Scope and validation

| | |
|---|---|
| **Changed** | Windows charge CLI and vendor backends, calibration reporting, the apps box, README |
| **Unchanged** | Power measurement, the conserved breakdown, charge behaviour on Lenovo, display calibration itself, macOS and Linux behaviour |
| **Tested on** | Windows 11 (16 logical cores, Energy Meter RAPL present, 225 % scale), on battery and on AC |
| **Verification** | Windows tests 35 → 42 plus the seven Python daemon tests; `--charge`, `--dry-run`, `repair` and `--charge-probe` run on this ThinkPad, and the dry run was confirmed to leave the saved limit and the EC untouched |
| **Not verified** | The HP backend has not run on HP hardware; Dell, ASUS, MSI and Acer are detection-only. A second display at a different scale was also unavailable for the cross-monitor paths (unit tested only) |

## Install

- **Windows** — `ClearPower-Setup-0.7.1-x64.exe` (per-user, no admin prompt; SmartScreen warns
  because the binary is not code-signed, choose *More info › Run anyway*) or the portable
  `ClearPower-0.7.1-x64-portable.zip`.
- **macOS** — the DMG. The 0.6.0 charging helper remains compatible, so no helper update is needed.
- **Linux** — `clearpower_0.7.1_all.deb`.

Verify downloads with the attached `SHA256SUMS`. Full list in
[CHANGELOG.md](https://github.com/Clearailhc/ClearPower/blob/main/CHANGELOG.md).

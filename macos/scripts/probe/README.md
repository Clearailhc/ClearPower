# Hardware probes (Phase 0)

Small C programs used to discover what this Mac exposes. Keep them: they are the
fastest way to produce a hardware report for a new machine / macOS version.

```bash
clang -o ioreport_probe ioreport_probe.c -framework CoreFoundation
clang -o smc_probe smc_probe.c -framework IOKit -framework CoreFoundation
clang -o brightness_probe brightness_probe.c -framework CoreGraphics -framework CoreFoundation
./ioreport_probe "Energy Model"      # per-block energy counters, 1 s delta
./smc_probe                          # charge-control / power / fan / battery keys
./smc_probe list > smc_keys.txt      # every key with type and value
./brightness_probe                   # DisplayServices brightness of each display
```

## Findings on MacBook Pro M3 Max, macOS 26.6 (2026-09-04)

**IOReport, no root needed.** Group `Energy Model` has per-block energy counters
(unit label per channel: mJ / uJ / nJ):
`CPU Energy`, `GPU Energy`, `ANE0`, `DRAM0`, `DCS0` + `AMCC0` (memory controller /
system cache), `DISP0` + `DISPEXT0` (display engines, *not* the panel backlight),
`ISP0`, `AVE0`, `MSR0` (media), `PCIe Port n Energy`.

**SMC (read without root, write needs root).** Multi-byte integers are little-endian.
- Charge control on this firmware ("Tahoe" key set): `CHTE` ui32 — `01 00 00 00`
  inhibits charging, `00 00 00 00` allows; `CHIE` hex_ 1 byte — `0x08` cuts the
  adapter (system runs from battery = "discharge"), `0x00` restores.
  Absent here: `CH0B`/`CH0C` (older: 0x02 inhibit), `CH0I` (older: 0x01 adapter off),
  `CHWA`, and the macOS-27-era firmware hysteresis keys `bfF0`/`bfD0`/`bfE0`
  (activation 0x02, upper/lower ui32 LE percent). Sources: charlie0129/batt `pkg/smc`.
- Live power: `PSTR` system total (W, flt), `PDTR` DC-in from adapter (W),
  `PPBR` battery power magnitude (W), `B0AC` battery current (si16 mA, negative =
  discharging), `B0AV` battery voltage (ui16 mV), `ID0R`/`VD0R` DC-in current/voltage.
  IOKit's `PowerTelemetryData` carries the same quantities but refreshes only every
  several seconds; SMC values are live.
- Temperatures: `Tp*` (CPU die sensors) and `Tg*` (GPU) flt °C; `TB0T` battery.
- Fans: `FNum`, `F0Ac`/`F1Ac` actual rpm (flt), `F0Mn`/`F0Mx` min/max.

**DisplayServices (private framework).** `DisplayServicesGetBrightness` /
`DisplayServicesSetBrightness(CGDirectDisplayID, float 0..1)` work from a user
session for the built-in display.

**IOKit `AppleSmartBattery`**: `CurrentCapacity` (%), `AppleRawCurrentCapacity` /
`AppleRawMaxCapacity` / `DesignCapacity` (mAh), `Voltage` (mV), `InstantAmperage`
(signed mA, 64-bit two's complement in ioreg output), `IsCharging`,
`ExternalConnected`, `FullyCharged`, `CycleCount`, `Temperature` (0.01 K),
`AdapterDetails{Watts, AdapterVoltage, Current, Description}`.

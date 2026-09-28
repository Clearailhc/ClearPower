import Foundation
import CoreGraphics
import Testing
import ClearPowerCore
@testable import MacBackend

struct BatteryCompatibilityTests {
    @Test func macOS27ChargingIgnoresMisleadingPPBR() {
        let props: [String: Any] = ["CurrentCapacity": 92, "ExternalConnected": true, "IsCharging": true,
            "Voltage": 13187, "InstantAmperage": 2674,
            "BatteryData": ["RemainingCapacity": 5424, "FullChargeCapacity": 5960, "DesignCapacity": 6249],
            "PowerTelemetryData": ["SystemPowerIn": 46373, "SystemLoad": 11098]]
        let sensors = ["PPBR": 0.553, "B0AC": 2651.0, "B0AV": 13190.0]
        let result = BatterySource.decode(props) { sensors[$0] }
        #expect(abs(result.d("bat_w") - 34.96669) < 1e-5)
        #expect(result.s("bat_status") == "Charging")
        #expect(result.b("bat_power_available"))
        #expect(abs(result.d("bat_full_wh") - 69.0168) < 1e-6)
        #expect(result.d("bat_energy_wh") > 60)
        #expect(abs(result.d("bat_design_wh") - 72.36342) < 1e-6)
        #expect(result.d("telemetry_dc_in_w") == 46.373)
    }

    @Test func legacyCapacityAndUnsignedDischargeCurrent() {
        let props: [String: Any] = ["CurrentCapacity": 50, "AppleRawCurrentCapacity": 3000,
            "AppleRawMaxCapacity": 6000, "DesignCapacity": 6249, "Voltage": 12000,
            "InstantAmperage": NSNumber(value: UInt64.max - 999), "ExternalConnected": false]
        let result = BatterySource.decode(props) { _ in nil }
        #expect(result.d("bat_w") == -12)
        #expect(result.s("bat_status") == "Discharging")
        #expect(abs(result.d("bat_energy_wh") - 34.74) < 1e-6)
    }

    @Test func zeroIsAValidFullBatteryAndMissingIsUnknown() {
        let full = BatterySource.decode(["CurrentCapacity": 100, "FullyCharged": true,
                                        "Voltage": 13000, "InstantAmperage": 0]) { _ in nil }
        #expect(full.b("bat_power_available"))
        #expect(full.d("bat_w") == 0)
        #expect(full.s("bat_status") == "Full")
        let absent = BatterySource.decode(["CurrentCapacity": 80]) { _ in .nan }
        #expect(!absent.b("bat_power_available"))
        #expect(absent.d("bat_full_wh") == -1)
    }

    @Test func smcIntegersUseObservedAppleSiliconByteOrder() {
        #expect(SMCValue(type: "si16", bytes: [0x5b, 0x0a]).float == 2651)
        #expect(SMCValue(type: "ui16", bytes: [0x86, 0x33]).float == 13190)
        #expect(SMCValue(type: "si16", bytes: [0x18, 0xfc]).float == -1000)
    }
}

struct PowerCompatibilityTests {
    @Test func disabledAdapterDoesNotEraseMeasuredFractionalInput() {
        var model = PowerModel(smoothingS: 5)
        var raw = RawPower(batW: -12.7, psys: 13.1, package: 1, core: 0.6, uncore: 0.4, dram: -1)
        raw.dcIn = 0.444; raw.adapterDisabled = true
        let result = model.update(raw: raw, onAC: true, now: 0, displayEmission: -1, displayOn: true)
        #expect(result.d("adapter_w") == 0.444)
        #expect(abs(result.d("sys_w") - 13.144) < 1e-9)
    }

    @Test func sourceTransitionsDoNotRetainChargingOrInputPower() {
        var model = PowerModel(smoothingS: 5)
        var raw = RawPower(batW: 35, psys: 15, package: 3, core: 2, uncore: 1, dram: 1)
        raw.dcIn = 50; raw.adapterDisabled = false
        _ = model.update(raw: raw, onAC: true, now: 0, displayEmission: -1, displayOn: true)
        raw.adapterDisabled = true; raw.dcIn = 0.444; raw.batW = -12.7
        let disabled = model.update(raw: raw, onAC: true, now: 1, displayEmission: -1, displayOn: true)
        #expect(disabled.d("adapter_w") == 0.444)
        #expect(disabled.d("bat_w") == -12.7)
        raw.dcIn = -1; raw.batW = -10
        let unplugged = model.update(raw: raw, onAC: false, now: 2, displayEmission: -1, displayOn: true)
        #expect(unplugged.d("adapter_w") == 0)
        #expect(unplugged.d("sys_w") == 10)
        raw.adapterDisabled = false; raw.dcIn = 20; raw.batW = 10
        let replugged = model.update(raw: raw, onAC: true, now: 3, displayEmission: -1, displayOn: true)
        #expect(replugged.d("adapter_w") == 20)
        #expect(replugged.d("sys_w") == 10)
        raw.dcIn = 0; raw.batW = -10
        let zero = model.update(raw: raw, onAC: true, now: 4, displayEmission: -1, displayOn: true)
        #expect(zero.d("adapter_w") == 0)
        #expect(zero.d("bat_w") == -10)
        #expect(zero.d("sys_w") == 10)
    }

    @Test func adapterReadbackUsesHardwareAndPreservesUnknown() {
        #expect(PlatformPower.adapterDisabled { $0 == "CHIE" ? SMCValue(type: "hex_", bytes: [8]) : nil } == true)
        #expect(PlatformPower.adapterDisabled { $0 == "CHIE" ? SMCValue(type: "hex_", bytes: [0]) : nil } == false)
        #expect(PlatformPower.adapterDisabled { _ in nil } == nil)
        #expect(PlatformPower.adapterDisabled { _ in SMCValue(type: "hex_", bytes: [255]) } == nil)
        #expect(PlatformPower.adapterDisabled { $0 == "CH0I" ? SMCValue(type: "hex_", bytes: [1]) : nil } == true)
    }
    @Test func measuredAdapterAndBatteryConserveBothChargingAndDischarging() {
        for battery in [35.0, 0.0, -12.0] {
            var model = PowerModel(smoothingS: 5)
            var raw = RawPower(batW: battery, psys: 14, package: 2, core: 1.5, uncore: 0.5, dram: -1)
            raw.dcIn = 50; raw.display = 6; raw.packageComplete = false
            let result = model.update(raw: raw, onAC: true, now: 10, displayEmission: 3.4, displayOn: true)
            #expect(result.d("adapter_w") == 50)
            #expect(result.d("sys_w") == 50 - battery)
            #expect(result.d("display_w") == 6)
            #expect(result.b("display_measured"))
            #expect(result.d("mem_w") == -1 && result.d("soc_w") == -1)
            let total = ["cpu_w", "gpu_w", "soc_w", "mem_w", "display_w", "other_w"].map { max(result.d($0), 0) }.reduce(0, +)
            #expect(abs(total - result.d("sys_w")) < 1e-9)
        }
    }

    @Test func stalledTransitionCannotLeaveUnattributedSmoothedPackagePower() {
        var model = PowerModel(smoothingS: 5)
        var raw = RawPower(batW: 0, psys: 20, package: 10, core: 2, uncore: 1, dram: 2)
        _ = model.update(raw: raw, onAC: true, now: 0, displayEmission: 3, displayOn: true)
        raw.packageComplete = false; raw.package = 3; raw.dram = -1
        let result = model.update(raw: raw, onAC: true, now: 1, displayEmission: 3, displayOn: true)
        let total = ["cpu_w", "gpu_w", "soc_w", "mem_w", "display_w", "other_w"].map { max(result.d($0), 0) }.reduce(0, +)
        #expect(abs(total - result.d("sys_w")) < 1e-9)
        #expect(result.d("soc_w") == -1)
    }

    @Test func missingBatteryCannotBeSubtractedFromInput() {
        var raw = RawPower(batW: 0, psys: 14, package: 2, core: -1, uncore: 2, dram: -1)
        raw.dcIn = 50; raw.batteryKnown = false; raw.packageComplete = false
        var model = PowerModel(smoothingS: 0)
        let result = model.update(raw: raw, onAC: true, now: 1, displayEmission: -1, displayOn: true)
        #expect(result.d("sys_w") == 14)
        #expect(result.d("cpu_w") == -1)
    }

    @Test func stalledCountersDoNotReturnAsHugeSpikes() {
        var health = EnergyHealth()
        let live = health.usable(cpu: 1)
        let zero = health.usable(cpu: 0)
        let stalled = health.usable(cpu: 0)
        let spike = health.usable(cpu: 800)
        #expect(live && !zero && !stalled && !spike)
    }

    @Test func cpuFallbackIsCompleteAndModelSpecific() {
        let rails = ["PC02": 4.0, "PC03": 0.5, "PC42": 5, "PC43": 0.6, "PP5b": 1.2, "PC12": 100]
        #expect(abs((PowerSensors.cpu(chip: "Apple M3 Max") { rails[$0] } ?? 0) - 11.3) < 1e-9)
        #expect(PowerSensors.cpu(chip: "Apple M4 Max") { rails[$0] } == nil)
        #expect(PowerSensors.cpu(chip: "Apple M3 Max") { $0 == "PP5b" ? nil : rails[$0] } == nil)
    }
}

struct WindowGeometryTests {
    @Test func sizesStayInPointsAcrossRetinaAndSmallDisplays() {
        let preferred = CGSize(width: 400, height: 700)
        // These are AppKit point coordinates; a 2x backing store must not double them.
        let retina = CGRect(x: 0, y: 38, width: 1512, height: 906)
        let external = CGRect(x: -1920, y: 0, width: 1920, height: 1055)
        #expect(WindowGeometry.contentSize(preferred: preferred, visibleFrame: retina) == preferred)
        #expect(WindowGeometry.contentSize(preferred: preferred, visibleFrame: external) == preferred)
        let small = WindowGeometry.contentSize(preferred: preferred, visibleFrame: CGRect(x: 0, y: 0, width: 360, height: 480))
        #expect(small == CGSize(width: 336, height: 456))
    }

    @Test func unpluggedScreenWindowsFitRemainingScreenWithNegativeOrigins() {
        let remaining = CGRect(x: -1280, y: 30, width: 1280, height: 690)
        let stranded = CGRect(x: 1700, y: -500, width: 520, height: 800)
        let moved = WindowGeometry.contained(stranded, in: remaining)
        #expect(remaining.contains(moved))
        #expect(moved.width == 520 && moved.height == 690)
        #expect(WindowGeometry.contained(moved, in: remaining) == moved)
    }
}

private final class FakeNative: NativeChargeControl {
    let limits = [80, 85, 90, 95, 100]
    var limit = 100
    var enabled = true
    var fail = false
    var writes: [Int] = []
    func state() throws -> (limit: Int, enabled: Bool) { (limit, enabled) }
    func set(_ value: Int) throws {
        if fail { throw ChargeError(errno: 5, "write failed") }
        limit = value; enabled = value < 100; writes.append(value)
    }
    func disable() throws { enabled = false }
}

struct ChargeCompatibilityTests {
    @Test func gatedKeysUseNativeLimitAndRestoreOriginal() throws {
        let native = FakeNative()
        let hw = SMCChargeHardware(stateDirectory: "/nonexistent", log: { _ in },
            read: { _ in nil }, write: { _, _ in -1 }, native: native)
        #expect(hw.method == .native)
        try hw.writeThresholds(start: 90, end: 95)
        hw.enforce(batPct: 100)
        hw.prepareForSleep(batPct: 100)
        #expect(native.writes == [95])
        #expect(hw.supportedLimits == [80, 85, 90, 95, 100])
        hw.reset()
        #expect(native.limit == 100)
    }

    @Test func unsupportedSavedLimitNeverSilentlyRaisesTo100() {
        let native = FakeNative()
        let hw = SMCChargeHardware(stateDirectory: "/nonexistent", log: { _ in },
            read: { _ in nil }, write: { _, _ in -1 }, native: native)
        #expect(throws: ChargeError.self) { try hw.writeThresholds(start: 65, end: 70) }
        hw.enforce(batPct: 80)
        #expect(!hw.lastError.isEmpty)
        #expect(native.writes.isEmpty)
    }

    @Test func nativeTopUpAcceptsDisabledLimitAt100() throws {
        let native = FakeNative()
        native.enabled = false
        let hw = SMCChargeHardware(stateDirectory: "/nonexistent", log: { _ in },
            read: { _ in nil }, write: { _, _ in -1 }, native: native)
        try hw.writeThresholds(start: 90, end: 95)
        try hw.writeThresholds(start: 95, end: 100)
        hw.enforce(batPct: 96)
        hw.enforce(batPct: 97)
        #expect(native.writes == [95, 100])
        #expect(hw.lastError.isEmpty)
    }

    @Test func adapterDischargeIsIndependentOfChargingKeys() throws {
        var adapter: [UInt8] = [0]
        let hw = SMCChargeHardware(stateDirectory: "/nonexistent", log: { _ in },
            read: { $0 == "CHIE" ? SMCValue(type: "hex_", bytes: adapter) : nil },
            write: { _, value in adapter = value; return 0 }, native: nil)
        #expect(hw.method == .none)
        #expect(hw.behaviours.contains("force-discharge"))
        try hw.writeBehaviour("force-discharge")
        #expect(adapter == [8])
        hw.reset()
        #expect(adapter == [0])
    }

    @Test func failedDischargeDoesNotChangeMode() {
        let hw = SMCChargeHardware(stateDirectory: "/nonexistent", log: { _ in },
            read: { $0 == "CHIE" ? SMCValue(type: "hex_", bytes: [0]) : nil },
            write: { _, _ in -2 }, native: FakeNative())
        let machine = ChargeStateMachine(hardware: hw)
        #expect(throws: ChargeError.self) { try machine.startDischarge(target: 95) }
        #expect(machine.mode == .limit && machine.target == 0)
    }

    @Test func legacyAndFirmwareStayPreferred() throws {
        for (keys, method) in [(["CHTE": [UInt8](repeating: 0, count: 4)], SMCChargeHardware.Method.tahoe),
                               (["bfF0": [0], "bfD0": [100, 0, 0, 0], "bfE0": [95, 0, 0, 0]], .firmware)] {
            var values = keys
            let native = FakeNative()
            let hw = SMCChargeHardware(stateDirectory: "/nonexistent", log: { _ in },
                read: { key in values[key].map { SMCValue(type: "hex_", bytes: $0) } },
                write: { key, bytes in values[key] = bytes; return 0 }, native: native)
            #expect(hw.method == method)
            try hw.writeThresholds(start: 75, end: 80)
            hw.enforce(batPct: 90)
            #expect(native.writes.isEmpty)
            if method == .tahoe { #expect(values["CHTE"] == [1, 0, 0, 0]) }
            else { #expect(values["bfD0"] == [80, 0, 0, 0]) }
        }
    }
}

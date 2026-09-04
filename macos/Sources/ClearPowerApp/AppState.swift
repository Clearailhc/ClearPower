// Central app state: engine (sampling), helper client (charge control), preferences,
// popover open/close bookkeeping. Plays the role of indicator.js's wiring.
import Foundation
import Combine
import AppKit
import ClearPowerCore
import MacBackend

@MainActor
final class AppState: ObservableObject {
    let engine = Engine()
    let helper = HelperClient()
    let prefs = Prefs()
    let sankey = SankeyModel()
    @Published private(set) var snapshot: [String: Any] = [:]
    @Published private(set) var topApps: [(name: String, w: Double)] = []
    @Published var popoverOpen = false
    @Published var lastError: String? = nil
    @Published var helperOnline = false
    private var cancellables = Set<AnyCancellable>()
    private var contentTimer: DispatchSourceTimer?
    private var hotTimer: DispatchSourceTimer?
    private let calibration = CalibrationWindow()
    private var wasCalibrating = false

    static let limits = [80, 90, 100]
    static let appMinW = 0.5
    static let contentIntervalS = 5.0

    init() {
        sankey.flowMode = prefs.flowAnimation
        sankey.reduceMotion = { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
        engine.onSample = { [weak self] snap in self?.onSample(snap) }
        helper.onChange = { [weak self] in
            guard let self = self else { return }
            self.helperOnline = self.helper.online
            self.engine.chargeState = self.helper.snapshotOverlay
            self.objectWillChange.send()
        }
        prefs.$flowAnimation.sink { [weak self] m in self?.sankey.flowMode = m }.store(in: &cancellables)
        prefs.$contentAware.sink { [weak self] _ in DispatchQueue.main.async { self?.syncContentTimer() } }.store(in: &cancellables)
        prefs.objectWillChange.sink { [weak self] in DispatchQueue.main.async { self?.objectWillChange.send() } }.store(in: &cancellables)
        calibration.onCancel = { [weak self] in self?.engine.cancelCalibration() }
        helper.refresh()
        engine.start()
    }

    // ---- sampling ----
    private func onSample(_ snap: [String: Any]) {
        snapshot = snap
        helper.refresh()
        if popoverOpen {
            sankey.update(snap)
            topApps = engine.topProcesses.map { ($0.name, $0.w) }
        }
        let running = snap.s("calib_state") == "running"
        if running {
            if !wasCalibrating { calibration.show() }
            calibration.update(progress: snap.d("calib_progress", 0))
        } else if wasCalibrating {
            calibration.hide()
        }
        if running != wasCalibrating { wasCalibrating = running; syncContentTimer() }
    }

    func setPopoverOpen(_ open: Bool) {
        popoverOpen = open
        sankey.setActive(open)
        hotTimer?.cancel(); hotTimer = nil
        if open {
            engine.touch()
            sankey.update(snapshot)
            helper.refresh()
            // Keep the engine at full rate for as long as the popover is open.
            let t = DispatchSource.makeTimerSource(queue: .main)
            t.schedule(deadline: .now(), repeating: 1.0)
            t.setEventHandler { [weak self] in self?.engine.touch() }
            hotTimer = t
            t.resume()
        }
        syncContentTimer()
    }

    // ---- derived text (port of _refreshRuntime / _updatePanel) ----
    var limit: Int { helper.limit }
    var mode: String { helper.mode }

    func runtimeText() -> String {
        let snap = snapshot
        let w = prefs.window
        if snap.s("calib_state") == "running" {
            return I18n.t("calibrating", ["p": Int((snap.d("calib_progress", 0) * 100).rounded())])
        }
        switch snap.s("bat_status") {
        case "Discharging":
            let m = snap.d("runtime_min_\(w)")
            if m > 0 {
                let approx = snap.i("runtime_basis_s", 0) < 300 ? "~" : ""
                return approx + I18n.t("remaining", ["t": I18n.fmtDuration(minutes: m)])
            }
            return I18n.t("estimating")
        case "Charging":
            let m = snap.d("eta_min_\(w)")
            return m > 0 ? I18n.t("toLimit", ["t": I18n.fmtDuration(minutes: m), "n": limit]) : I18n.t("charging")
        default:
            if snap.b("on_ac") {
                return snap.i("bat_pct", 0) >= limit - 1 ? I18n.t("atLimit") : I18n.t("pluggedIn")
            }
            return ""
        }
    }

    var windowButtonVisible: Bool {
        let st = snapshot.s("bat_status")
        return st == "Discharging" || st == "Charging"
    }

    func panelText() -> String {
        let snap = snapshot
        guard !snap.isEmpty else { return "" }
        let w = fmtW(snap.d("sys_w"), digits: 1)
        let p = "\(snap.i("bat_pct", 0))%"
        var rt = p
        if snap.s("bat_status") == "Discharging" {
            let m = snap.d("runtime_min_\(prefs.window)")
            if m > 0 { rt = I18n.fmtDuration(minutes: m) }
        }
        switch prefs.panelText {
        case "watts": return w
        case "percent": return p
        case "both": return "\(w) · \(p)"
        case "runtime": return rt
        case "none": return ""
        default: return w
        }
    }

    func healthText() -> String {
        let snap = snapshot
        let design = snap.d("bat_design_wh", 0), full = snap.d("bat_full_wh", 0)
        guard design > 0 else { return "" }
        return I18n.t("health", ["p": Int((100 * full / design).rounded()), "full": String(format: "%.1f", full),
                                  "design": String(format: "%.1f", design), "n": snap.i("cycle_count", 0)])
    }

    func tempsText() -> String {
        let snap = snapshot
        var parts: [String] = []
        if snap.d("temp_cpu") >= 0 { parts.append("CPU \(Int(snap.d("temp_cpu").rounded()))°") }
        if snap.d("temp_gpu") >= 0 { parts.append("GPU \(Int(snap.d("temp_gpu").rounded()))°") }
        if snap.d("temp_nvme") >= 0 { parts.append("SSD \(Int(snap.d("temp_nvme").rounded()))°") }
        if snap.d("temp_bat") >= 0 { parts.append(I18n.t("battery") + " \(Int(snap.d("temp_bat").rounded()))°") }
        if snap.i("fan1", -1) > 0 { parts.append("\(snap.i("fan1")) rpm") }
        return parts.joined(separator: " · ")
    }

    // ---- charge control ----
    private func fail(_ e: String) {
        var msg = e
        if e.range(of: "not permitted|permission|not authorized", options: [.regularExpression, .caseInsensitive]) != nil { msg = I18n.t("errPermission") }
        else if e.range(of: "not supported", options: .caseInsensitive) != nil { msg = I18n.t("errUnsupported") }
        else if e.range(of: "not installed", options: .caseInsensitive) != nil { msg = I18n.t("helperMissing") }
        lastError = msg
        NSLog("ClearPower: %@", e)
        DispatchQueue.main.asyncAfter(deadline: .now() + 4) { [weak self] in if self?.lastError == msg { self?.lastError = nil } }
    }

    private func done(_ err: String?) { if let e = err { fail(e) } }

    func cycleLimit() {
        let cur = limit
        let i = Self.limits.firstIndex(of: cur) ?? -1
        let next = Self.limits[(i + 1) % Self.limits.count]
        helper.setChargeLimit(next, completion: done)
    }

    func setLimit(_ pct: Int, completion: @escaping (String?) -> Void = { _ in }) {
        helper.setChargeLimit(pct) { [weak self] e in self?.done(e); completion(e) }
    }

    func toggleDischarge() {
        if mode == "discharge" { helper.cancelSpecial(completion: done) } else { helper.startDischarge(0, completion: done) }
    }

    func toggleTopUp() {
        if mode == "topup" { helper.cancelSpecial(completion: done) } else { helper.startTopUp(completion: done) }
    }

    func setPowerMode(_ m: Int) { helper.setPowerMode(m, completion: done) }

    func cycleWindow() {
        let i = Prefs.windows.firstIndex(of: prefs.window) ?? 0
        prefs.runtimeWindow = Prefs.windows[(i + 1) % Prefs.windows.count]
    }

    // ---- screen content sampling (OLED display estimate) ----
    private func contentWanted() -> Bool {
        guard prefs.contentAware else { return false }
        return popoverOpen || snapshot.s("calib_state") == "running"
    }

    private func syncContentTimer() {
        let want = contentWanted()
        if want && contentTimer == nil {
            let t = DispatchSource.makeTimerSource(queue: .main)
            t.schedule(deadline: .now(), repeating: Self.contentIntervalS)
            t.setEventHandler { [weak self] in self?.sampleContent() }
            contentTimer = t
            t.resume()
        } else if !want, let t = contentTimer {
            t.cancel(); contentTimer = nil
        }
    }

    private func sampleContent() {
        Task { @MainActor in
            let apl = await ScreenLuminance.sample()
            if apl >= 0 { self.engine.setDisplayContent(apl) }
        }
    }

    func calibrateDisplay() { engine.calibrateDisplay() }
    func cancelCalibration() { engine.cancelCalibration() }
    var helperCanControl: Bool { helperOnline && helper.controlSupported }
}

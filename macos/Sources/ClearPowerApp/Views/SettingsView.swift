// Preferences window. Port of prefs.js plus the macOS-only rows (helper, login item).
import SwiftUI
import ServiceManagement
import ClearPowerCore

struct SettingsView: View {
    @EnvironmentObject var state: AppState
    @ObservedObject private var prefs: Prefs
    @State private var limitValue = 80
    @State private var limitMessage = ""
    @State private var limitTask: DispatchWorkItem? = nil
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @State private var helperMessage = ""

    init(prefs: Prefs) { self.prefs = prefs }

    var body: some View {
        Form {
            Section(I18n.t("prefsTopBar")) {
                Picker(I18n.t("prefsPanelText"), selection: $prefs.panelText) {
                    Text(I18n.t("panelWatts")).tag("watts"); Text(I18n.t("panelPercent")).tag("percent")
                    Text(I18n.t("panelBoth")).tag("both"); Text(I18n.t("panelRuntime")).tag("runtime"); Text(I18n.t("panelNone")).tag("none")
                }
                Picker(I18n.t("prefsFlow"), selection: $prefs.flowAnimation) {
                    Text(I18n.t("flowAlways")).tag("always"); Text(I18n.t("flowOnAc")).tag("on-ac"); Text(I18n.t("flowNever")).tag("never")
                }
                Text(I18n.t("prefsFlowSub")).font(.caption).foregroundColor(.secondary)
                Toggle(I18n.t("prefsShowIcon"), isOn: $prefs.showPanelIcon)
                Picker(I18n.t("prefsLanguage"), selection: $prefs.language) {
                    Text(I18n.t("langSystem")).tag("system"); Text(I18n.t("langEn")).tag("en"); Text(I18n.t("langZh")).tag("zh-cn")
                }
                Toggle(I18n.t("launchAtLogin"), isOn: $launchAtLogin)
                    .onChange(of: launchAtLogin) { _, on in
                        do { if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() } }
                        catch { helperMessage = "\(error.localizedDescription)" }
                    }
            }
            Section(I18n.t("prefsCharge")) {
                Stepper(value: $limitValue, in: 50...100) {
                    HStack { Text(I18n.t("prefsLimit")); Spacer(); Text("\(limitValue) %").monospacedDigit() }
                }
                .onChange(of: limitValue) { _, v in scheduleLimit(v) }
                .disabled(!state.helperCanControl)
                Text(limitMessage.isEmpty ? I18n.t("prefsLimitSub") : limitMessage).font(.caption).foregroundColor(.secondary)
                Text(I18n.t("sleepNote")).font(.caption).foregroundColor(.secondary)
                helperRow
            }
            Section(I18n.t("prefsRuntime")) {
                Picker(I18n.t("prefsWindow"), selection: $prefs.runtimeWindow) {
                    Text(I18n.t("win10")).tag(10); Text(I18n.t("win30")).tag(30); Text(I18n.t("win60")).tag(60)
                }
                Text(I18n.t("prefsWindowSub")).font(.caption).foregroundColor(.secondary)
            }
            Section(I18n.t("prefsDisplay")) {
                Toggle(I18n.t("prefsContent"), isOn: $prefs.contentAware)
                Text(I18n.t("prefsContentSub")).font(.caption).foregroundColor(.secondary)
                HStack(alignment: .top) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(I18n.t("prefsCalibrateTitle"))
                        Text(I18n.t("prefsCalibrateSub")).font(.caption).foregroundColor(.secondary)
                    }
                    Spacer()
                    Button(I18n.t("prefsCalibrate")) { state.calibrateDisplay() }
                        .disabled(state.snapshot.s("calib_state") == "running")
                }
                Text(calibrationStatus).font(.caption).foregroundColor(.secondary)
            }
            Section {
                HStack {
                    Text(I18n.t("aboutVersion", ["v": ClearPowerVersion.string])).font(.caption).foregroundColor(.secondary)
                    Spacer()
                    Button(I18n.t("quit")) { NSApp.terminate(nil) }
                }
            }
        }
        .formStyle(.grouped)
        .frame(width: 520, height: 640)
        .onAppear { limitValue = state.limit }
        .onChange(of: state.limit) { _, v in if limitTask == nil { limitValue = v } }
        .id(prefs.langVersion)
    }

    private var helperRow: some View {
        let installed = HelperInstaller.installed
        let online = state.helperOnline
        let v = state.helper.helperVersion
        let outdated = online && !v.isEmpty && v != ClearPowerVersion.string
        return VStack(alignment: .leading, spacing: 4) {
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text(I18n.t("prefsHelper"))
                    Text(online ? (outdated ? I18n.t("helperUpdateNeeded", ["v": v]) : I18n.t("helperInstalled", ["v": v]))
                         : I18n.t("helperMissing")).font(.caption).foregroundColor(.secondary)
                    Text(I18n.t("helperExplain")).font(.caption).foregroundColor(.secondary)
                }
                Spacer()
                Button(I18n.t("helperInstall")) { run(HelperInstaller.install) }
                if installed { Button(I18n.t("helperRemove")) { run(HelperInstaller.remove) } }
            }
            if !helperMessage.isEmpty { Text(helperMessage).font(.caption).foregroundColor(.red) }
        }
    }

    private func run(_ f: () -> String?) {
        helperMessage = f().map { I18n.t("installFailed", ["m": $0]) } ?? ""
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { state.helper.refresh() }
    }

    private func scheduleLimit(_ v: Int) {
        limitTask?.cancel()
        let task = DispatchWorkItem {
            state.setLimit(v) { err in
                limitMessage = err ?? ""
                limitTask = nil
            }
        }
        limitTask = task
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6, execute: task)
    }

    private var calibrationStatus: String {
        let snap = state.snapshot
        if snap.s("calib_state") == "running" {
            return I18n.t("calibrating", ["p": Int((snap.d("calib_progress", 0) * 100).rounded())])
        }
        var s: String
        if snap.b("display_calibrated") {
            let at = snap.d("calibrated_at", 0)
            let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd HH:mm"
            s = I18n.t("calibratedOn", ["d": at > 0 ? f.string(from: Date(timeIntervalSince1970: at)) : "–"])
        } else {
            s = I18n.t("notCalibrated")
        }
        let msg = snap.s("calib_message")
        if !msg.isEmpty { s += "\n" + I18n.t("calibFailed", ["m": msg]) }
        return s
    }
}

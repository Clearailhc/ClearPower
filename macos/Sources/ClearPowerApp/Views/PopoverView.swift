// The popover, section for section as in indicator.js: offline banner, header (limit pill,
// discharge, top up, settings), battery bar + runtime + health, Sankey, power modes +
// temps, top apps.
import SwiftUI
import ClearPowerCore

struct PopoverView: View {
    @EnvironmentObject var state: AppState
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        let snap = state.snapshot
        let online = state.helperOnline
        VStack(alignment: .leading, spacing: 10) {
            if !online {
                HStack {
                    Text(I18n.t("daemonOffline")).foregroundColor(Color(red: 0.88, green: 0.48, blue: 0.37))
                    Spacer()
                    Button(I18n.t("helperInstall")) { install() }
                }
            }
            header
            if let e = state.lastError {
                Text(e).font(.caption).foregroundColor(Color(red: 0.88, green: 0.48, blue: 0.37))
            }
            VStack(alignment: .leading, spacing: 6) {
                BatteryBarView(pct: snap.i("bat_pct", 0), limit: state.limit, status: snap.s("bat_status"),
                               mode: state.mode, onAc: snap.b("on_ac"))
                HStack {
                    Text(state.runtimeText()).font(.callout).foregroundColor(.secondary)
                    Spacer()
                    if state.windowButtonVisible {
                        Button(I18n.t("win\(state.prefs.window)")) { state.cycleWindow() }
                            .buttonStyle(MiniButtonStyle())
                    }
                }
                Text(state.healthText()).font(.callout).foregroundColor(.secondary)
            }
            SankeyView(model: state.sankey)
            powerRow
            appsBox
            HStack {
                Spacer()
                Button(I18n.t("quit")) { NSApp.terminate(nil) }.buttonStyle(.plain).font(.caption).foregroundColor(.secondary)
            }
        }
        .padding(EdgeInsets(top: 10, leading: 12, bottom: 14, trailing: 12))
        .frame(width: 400)
        .onAppear { state.setPopoverOpen(true) }
        .onDisappear { state.setPopoverOpen(false) }
        .id(state.prefs.langVersion)
    }

    private var header: some View {
        HStack(spacing: 8) {
            Button(I18n.t("limit", ["n": state.limit])) { state.cycleLimit() }
                .buttonStyle(PillButtonStyle(checked: false))
                .disabled(!(state.mode == "limit" && state.helperCanControl))
                .opacity(state.mode == "limit" && state.helperCanControl ? 1 : 0.6)
            Spacer()
            if state.helper.dischargeSupported {
                Button {
                    state.toggleDischarge()
                } label: {
                    Label(state.mode == "discharge" ? I18n.t("dischargingTo", ["n": state.helper.target]) : I18n.t("discharge"), systemImage: "minus")
                }
                .buttonStyle(PillButtonStyle(checked: state.mode == "discharge"))
                .disabled(!state.helperCanControl)
            }
            Button {
                state.toggleTopUp()
            } label: {
                Label(state.mode == "topup" ? I18n.t("toppingUp") : I18n.t("topUp"), systemImage: "plus")
            }
            .buttonStyle(PillButtonStyle(checked: state.mode == "topup"))
            .disabled(!state.helperCanControl)
            Button {
                openWindow(id: "settings")
                NSApp.activate(ignoringOtherApps: true)
            } label: {
                Image(systemName: "gearshape")
            }
            .buttonStyle(PillButtonStyle(checked: false, iconOnly: true))
        }
    }

    private var powerRow: some View {
        let snap = state.snapshot
        let active = snap.s("platform_profile")
        let high = snap.b("high_power_supported")
        return HStack(spacing: 6) {
            modeButton("leaf", "low-power", 1, active)
            modeButton("dial.medium", "automatic", 0, active)
            if high { modeButton("bolt", "high-power", 2, active) }
            Spacer()
            Text(state.tempsText()).font(.callout).foregroundColor(.secondary)
        }
    }

    private func modeButton(_ icon: String, _ id: String, _ mode: Int, _ active: String) -> some View {
        Button { state.setPowerMode(mode) } label: { Image(systemName: icon) }
            .buttonStyle(PillButtonStyle(checked: active == id, iconOnly: true))
            .disabled(!state.helperOnline)
            .help(I18n.t(id == "low-power" ? "powerLow" : (id == "high-power" ? "powerHigh" : "powerAuto")))
    }

    private var appsBox: some View {
        let sig = state.topApps.filter { $0.w >= AppState.appMinW }
        return VStack(alignment: .leading, spacing: 4) {
            if sig.isEmpty {
                HStack { Spacer(); Text(I18n.t("noApps")).font(.callout).foregroundColor(.secondary); Spacer() }
            } else {
                ForEach(Array(sig.enumerated()), id: \.offset) { _, app in
                    HStack {
                        Text(app.name).lineLimit(1)
                        Spacer()
                        Text(fmtW(app.w)).foregroundColor(.secondary)
                    }
                }
            }
        }
        .padding(EdgeInsets(top: 8, leading: 12, bottom: 8, trailing: 12))
        .frame(maxWidth: .infinity)
        .background(RoundedRectangle(cornerRadius: 14).fill(Color(white: 0.5, opacity: 0.12)))
    }

    private func install() {
        if let e = HelperInstaller.install() {
            state.lastError = I18n.t("installFailed", ["m": e])
        } else {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) { state.helper.refresh() }
        }
    }
}

struct PillButtonStyle: ButtonStyle {
    var checked: Bool
    var iconOnly = false
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .padding(EdgeInsets(top: 4, leading: iconOnly ? 8 : 12, bottom: 4, trailing: iconOnly ? 8 : 12))
            .background(RoundedRectangle(cornerRadius: 14).fill(checked ? Color(red: 0.36, green: 0.60, blue: 0.92, opacity: 0.40)
                                                                    : Color(white: 0.5, opacity: configuration.isPressed ? 0.28 : 0.16)))
            .contentShape(RoundedRectangle(cornerRadius: 14))
    }
}

struct MiniButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 11))
            .padding(EdgeInsets(top: 1, leading: 10, bottom: 1, trailing: 10))
            .background(RoundedRectangle(cornerRadius: 10).fill(Color(white: 0.5, opacity: configuration.isPressed ? 0.28 : 0.16)))
    }
}

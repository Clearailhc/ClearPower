import SwiftUI
import AppKit
import ClearPowerCore

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)  // menu bar only, also when run outside a bundle
    }
}

struct ClearPowerApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var state = AppState()

    var body: some Scene {
        MenuBarExtra {
            PopoverView().environmentObject(state)
        } label: {
            MenuBarLabel().environmentObject(state)
        }
        .menuBarExtraStyle(.window)

        Window("ClearPower", id: "settings") {
            SettingsView(prefs: state.prefs).environmentObject(state)
        }
        .windowResizability(.contentMinSize)
        .defaultPosition(.center)
    }
}

struct MenuBarLabel: View {
    @EnvironmentObject var state: AppState
    var body: some View {
        let text = state.panelText()
        HStack(spacing: 3) {
            if state.prefs.showPanelIcon || text.isEmpty {
                Image(systemName: "bolt.horizontal").opacity(state.helperOnline ? 1 : 0.5)
            }
            if !text.isEmpty { Text(text).monospacedDigit() }
        }
    }
}

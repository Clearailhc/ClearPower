// User preferences: the same six keys as the GNOME gschema, stored in UserDefaults.
import Foundation
import Combine
import ClearPowerCore

final class Prefs: ObservableObject {
    static let panelTextValues = ["watts", "percent", "both", "runtime", "none"]
    static let flowValues = ["always", "on-ac", "never"]
    static let languageValues = ["system", "en", "zh-cn"]
    static let windows = [10, 30, 60]

    private let d = UserDefaults.standard

    @Published var panelText: String { didSet { d.set(panelText, forKey: "panel-text") } }
    @Published var flowAnimation: String { didSet { d.set(flowAnimation, forKey: "flow-animation") } }
    @Published var runtimeWindow: Int { didSet { d.set(runtimeWindow, forKey: "runtime-window") } }
    @Published var language: String {
        didSet { d.set(language, forKey: "language"); I18n.setLanguage(language); langVersion += 1 }
    }
    @Published var showPanelIcon: Bool { didSet { d.set(showPanelIcon, forKey: "show-panel-icon") } }
    @Published var contentAware: Bool { didSet { d.set(contentAware, forKey: "content-aware") } }
    /// Bumped on language change so views re-read their strings.
    @Published var langVersion = 0

    init() {
        d.register(defaults: ["panel-text": "watts", "flow-animation": "on-ac", "runtime-window": 30,
                              "language": "system", "show-panel-icon": false, "content-aware": false])
        panelText = d.string(forKey: "panel-text") ?? "watts"
        flowAnimation = d.string(forKey: "flow-animation") ?? "on-ac"
        let w = d.integer(forKey: "runtime-window")
        runtimeWindow = Self.windows.contains(w) ? w : 30
        language = d.string(forKey: "language") ?? "system"
        showPanelIcon = d.bool(forKey: "show-panel-icon")
        contentAware = d.bool(forKey: "content-aware")
        I18n.setLanguage(language)
    }

    var window: Int { Self.windows.contains(runtimeWindow) ? runtimeWindow : 30 }
}

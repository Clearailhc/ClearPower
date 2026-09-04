// Installs / removes the privileged helper through a one-time administrator prompt.
// This is the no-Developer-ID path (SMAppService needs a consistent signing identity);
// scripts/build-app.sh can switch to SMAppService when SIGN_IDENTITY is set.
import Foundation
import ClearPowerIPC

enum HelperInstaller {
    struct Paths {
        var script: String
        var helper: String
        var plist: String
    }

    /// Locations inside the app bundle (or the SwiftPM build directory in development).
    static func bundledPaths() -> Paths? {
        let bundle = Bundle.main
        if let s = bundle.path(forResource: "install-helper", ofType: "sh"),
           let p = bundle.path(forResource: "org.clearpower.helper", ofType: "plist") {
            let helper = (bundle.executablePath! as NSString).deletingLastPathComponent + "/org.clearpower.helper"
            if FileManager.default.fileExists(atPath: helper) { return Paths(script: s, helper: helper, plist: p) }
        }
        // Development: next to the app binary in .build/<config>/, sources under macos/.
        let exeDir = (Bundle.main.executablePath! as NSString).deletingLastPathComponent
        let root = URL(fileURLWithPath: exeDir).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let s = root.appendingPathComponent("scripts/install-helper.sh").path
        let p = root.appendingPathComponent("Resources/org.clearpower.helper.plist").path
        let h = exeDir + "/clearpower-helper"
        if [s, p, h].allSatisfy({ FileManager.default.fileExists(atPath: $0) }) { return Paths(script: s, helper: h, plist: p) }
        return nil
    }

    static var installed: Bool { FileManager.default.fileExists(atPath: helperInstallPath) }

    private static func shellQuote(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }

    /// Runs `command` as root with the standard macOS administrator prompt. Returns an error
    /// message or nil. Must be called on the main thread.
    static func runPrivileged(_ command: String) -> String? {
        let escaped = command.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
        let src = "do shell script \"\(escaped)\" with administrator privileges"
        var err: NSDictionary?
        guard let script = NSAppleScript(source: src) else { return "cannot build AppleScript" }
        script.executeAndReturnError(&err)
        if let err = err {
            let code = err[NSAppleScript.errorNumber] as? Int ?? 0
            if code == -128 { return "cancelled" }
            return (err[NSAppleScript.errorMessage] as? String) ?? "error \(code)"
        }
        return nil
    }

    static func install() -> String? {
        guard let p = bundledPaths() else { return "helper files not found in the app bundle" }
        return runPrivileged("/bin/sh \(shellQuote(p.script)) install \(shellQuote(p.helper)) \(shellQuote(p.plist))")
    }

    static func remove() -> String? {
        guard let p = bundledPaths() else {
            return runPrivileged("/bin/launchctl bootout system/\(helperLabel); /bin/rm -f \(helperInstallPath) \(helperPlistPath)")
        }
        return runPrivileged("/bin/sh \(shellQuote(p.script)) remove")
    }
}

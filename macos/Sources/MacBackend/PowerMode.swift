// macOS power modes (pmset powermode / lowpowermode). Reading spawns pmset, so it is only
// refreshed while someone is looking. Setting needs root and goes through the helper.
import Foundation

public enum PowerMode {
    /// Snapshot key platform_profile: "low-power" | "automatic" | "high-power" | "" (unknown).
    /// Also returns whether the machine supports high power mode.
    public static func read() -> (profile: String, highPowerSupported: Bool) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/pmset")
        p.arguments = ["-g"]
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return ("", false) }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        let text = String(data: data, encoding: .utf8) ?? ""
        var powermode: Int? = nil, lowpower: Int? = nil
        for line in text.split(separator: "\n") {
            let parts = line.split(separator: " ", omittingEmptySubsequences: true)
            guard parts.count >= 2 else { continue }
            if parts[0] == "powermode" { powermode = Int(parts[1]) }
            if parts[0] == "lowpowermode" { lowpower = Int(parts[1]) }
        }
        if let pm = powermode {
            return ([0: "automatic", 1: "low-power", 2: "high-power"][pm] ?? "", true)
        }
        if let lp = lowpower { return (lp == 1 ? "low-power" : "automatic", false) }
        return (ProcessInfo.processInfo.isLowPowerModeEnabled ? "low-power" : "automatic", false)
    }
}

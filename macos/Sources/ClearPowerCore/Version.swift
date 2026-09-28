import Foundation

public enum ClearPowerVersion {
    /// Kept in sync with the repository's VERSION file by scripts/build-app.sh (-D flag not
    /// available for plain SwiftPM builds, so the value is substituted into this file).
    public static let string = "0.6.1"
    /// 0.6.1 changes telemetry/UI only; the 0.6.0 charging helper is compatible.
    public static let minimumHelperVersion = "0.6.0"
}

import Foundation

public enum ClearPowerVersion {
    /// Kept in sync with the repository's VERSION file by scripts/build-app.sh (-D flag not
    /// available for plain SwiftPM builds, so the value is substituted into this file).
    public static let string = "0.3.0"
}

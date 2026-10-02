import Foundation

public enum ClearPowerVersion {
    /// Kept in sync with the repository's VERSION file by scripts/build-app.sh (-D flag not
    /// available for plain SwiftPM builds, so the value is substituted into this file).
    public static let string = "0.7.0"
    /// 0.7.0 does not touch the charging protocol; the 0.6.0 helper stays compatible.
    public static let minimumHelperVersion = "0.6.0"
}

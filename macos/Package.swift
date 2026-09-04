// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "ClearPower",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "ClearPower", targets: ["ClearPowerApp"]),
        .executable(name: "clearpower-helper", targets: ["ClearPowerHelper"]),
    ],
    targets: [
        // Platform-independent logic: smoothing, runtime estimate, conserved breakdown,
        // charge state machine, display calibration, i18n. Mirrors daemon/clearpowerd + extension.
        .target(name: "ClearPowerCore"),
        // C shims for private/awkward system APIs: SMC user client, IOReport, DisplayServices.
        .target(
            name: "CSupport",
            linkerSettings: [
                .linkedFramework("IOKit"),
                .linkedFramework("CoreFoundation"),
                .linkedFramework("CoreGraphics"),
            ]),
        // XPC protocol shared by the app and the privileged helper.
        .target(name: "ClearPowerIPC"),
        // macOS hardware sources (replaces daemon/clearpowerd/sources + sysfs).
        .target(name: "MacBackend", dependencies: ["ClearPowerCore", "CSupport"]),
        // Root launchd daemon: charge control only.
        .executableTarget(
            name: "ClearPowerHelper",
            dependencies: ["ClearPowerCore", "CSupport", "ClearPowerIPC", "MacBackend"]),
        // Menu bar app.
        .executableTarget(
            name: "ClearPowerApp",
            dependencies: ["ClearPowerCore", "MacBackend", "ClearPowerIPC"]),
        .testTarget(
            name: "ClearPowerCoreTests",
            dependencies: ["ClearPowerCore"],
            resources: [.copy("Fixtures")]),
    ],
    swiftLanguageVersions: [.v5]
)

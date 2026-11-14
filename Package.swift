// swift-tools-version:5.10
import PackageDescription

let package = Package(
    name: "ShiftHappens",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "shift-happens", targets: ["ShiftHappensApp"]),
    ],
    targets: [
        // Pure logic: layout mapping, smart decision, spelling choice. No AppKit.
        .target(name: "ShiftCore"),
        // macOS glue that tests can reach: layout reading, spell checker.
        .target(name: "ShiftMac", dependencies: ["ShiftCore"]),
        // Menu-bar app: hotkeys, clipboard, settings window.
        .executableTarget(name: "ShiftHappensApp", dependencies: ["ShiftCore", "ShiftMac"]),
        .testTarget(name: "ShiftCoreTests", dependencies: ["ShiftCore"]),
        .testTarget(name: "ShiftMacTests", dependencies: ["ShiftCore", "ShiftMac"]),
    ]
)

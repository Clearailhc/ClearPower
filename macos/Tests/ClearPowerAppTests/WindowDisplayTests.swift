import AppKit
import SwiftUI
import Testing
@testable import ClearPowerApp

@MainActor
@Suite(.serialized)
struct WindowDisplayTests {
    @Test func popoverFitsLogicalWidthAfterDisplayNotification() async throws {
        _ = NSApplication.shared
        let state = AppState()
        state.engine.stop()
        let window = NSWindow(contentRect: NSRect(x: 100, y: 100, width: 400, height: 440),
                              styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        defer { state.setPopoverOpen(false); window.close() }
        let host = NSHostingView(rootView: PopoverView().environmentObject(state))
        window.contentView = host
        try await Task.sleep(nanoseconds: 300_000_000)
        host.layoutSubtreeIfNeeded()
        let before = host.fittingSize
        NotificationCenter.default.post(name: NSApplication.didChangeScreenParametersNotification, object: nil)
        try await Task.sleep(nanoseconds: 300_000_000)
        host.layoutSubtreeIfNeeded()
        #expect(abs(host.fittingSize.width - 400) < 1)
        #expect(abs(host.fittingSize.width - before.width) < 1)
        #expect(host.fittingSize.height > 100)
        if let screen = window.screen { #expect(host.fittingSize.height <= screen.visibleFrame.height) }
    }

    @Test func hostWindowRefreshesWithoutDoublingPointSize() async throws {
        _ = NSApplication.shared
        let window = NSWindow(contentRect: NSRect(x: 100, y: 100, width: 400, height: 300),
                              styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        defer { window.close() }
        var observed: WindowDisplayMetrics?
        let view = WindowDisplayObserver.DisplayView()
        view.onChange = { observed = $0 }
        window.contentView = view
        try await Task.sleep(nanoseconds: 250_000_000)
        if let screen = window.screen ?? NSScreen.screens.first {
            #expect(observed?.scale == screen.backingScaleFactor)
            #expect(observed?.visibleFrame == screen.visibleFrame)
        }
        NotificationCenter.default.post(name: NSApplication.didChangeScreenParametersNotification, object: nil)
        NotificationCenter.default.post(name: NSWindow.didChangeBackingPropertiesNotification, object: window)
        try await Task.sleep(nanoseconds: 250_000_000)
        #expect(window.contentView?.frame.width == 400)
        #expect(window.contentView?.frame.height == 300)
        window.contentView = nil
        // Pending display notifications must not retain or access a detached window.
        NotificationCenter.default.post(name: NSApplication.didChangeScreenParametersNotification, object: nil)
    }

    @Test func shortScreenReservesSpaceForSettingsTitleBar() {
        let display = WindowDisplayMetrics(visibleFrame: CGRect(x: 0, y: 0, width: 800, height: 480), chromeHeight: 52)
        let content = display.size(width: 520, height: 640)
        #expect(content.height + display.chromeHeight + 24 == 480)
        #expect(content.width == 520)
    }
}

import AppKit
import SwiftUI
import MacBackend

struct WindowDisplayMetrics: Equatable {
    var screenID: UInt32 = 0
    var scale: CGFloat = 2
    var visibleFrame = CGRect(x: 0, y: 0, width: 1024, height: 768)
    var chromeHeight: CGFloat = 0

    func size(width: CGFloat, height: CGFloat) -> CGSize {
        let contentArea = CGRect(origin: visibleFrame.origin,
                                 size: CGSize(width: visibleFrame.width, height: max(1, visibleFrame.height - chromeHeight)))
        return WindowGeometry.contentSize(preferred: CGSize(width: width, height: height), visibleFrame: contentArea)
    }
}

/// Observes the actual hosting window rather than NSScreen.main (which may be
/// another app's screen). AppKit owns pixel scaling; SwiftUI sizes stay in points.
struct WindowDisplayObserver: NSViewRepresentable {
    var onChange: (WindowDisplayMetrics) -> Void

    func makeNSView(context: Context) -> DisplayView {
        let view = DisplayView()
        view.onChange = onChange
        return view
    }
    func updateNSView(_ view: DisplayView, context: Context) { view.onChange = onChange }

    final class DisplayView: NSView {
        var onChange: ((WindowDisplayMetrics) -> Void)?
        private var observers: [NSObjectProtocol] = []
        private var pending: DispatchWorkItem?
        private var last: WindowDisplayMetrics?

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            observers.forEach(NotificationCenter.default.removeObserver)
            observers.removeAll()
            pending?.cancel()
            last = nil
            guard let window else { return }
            for name in [NSWindow.didChangeScreenNotification, NSWindow.didChangeBackingPropertiesNotification,
                         NSWindow.didBecomeKeyNotification] {
                observers.append(NotificationCenter.default.addObserver(forName: name, object: window, queue: .main) { [weak self] _ in
                    self?.scheduleRefresh()
                })
            }
            observers.append(NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                                                    object: nil, queue: .main) { [weak self] _ in
                self?.scheduleRefresh()
            })
            scheduleRefresh()
        }

        override func viewDidChangeBackingProperties() {
            super.viewDidChangeBackingProperties()
            scheduleRefresh()
        }

        private func scheduleRefresh() {
            pending?.cancel()
            // Display reconfiguration emits a burst; wait for AppKit's final geometry.
            let work = DispatchWorkItem { [weak self] in self?.refresh() }
            pending = work
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1, execute: work)
        }

        private func refresh() {
            guard let window else { return }
            let screens = NSScreen.screens
            let screen = screens.first(where: { $0 == window.screen }) ?? screens.max(by: {
                let a = $0.frame.intersection(window.frame), b = $1.frame.intersection(window.frame)
                return (a.isNull ? 0 : a.width * a.height) < (b.isNull ? 0 : b.width * b.height)
            })
            guard let screen else { return }
            let metrics = WindowDisplayMetrics(
                screenID: (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0,
                scale: screen.backingScaleFactor, visibleFrame: screen.visibleFrame,
                chromeHeight: max(0, window.frame.height - window.contentLayoutRect.height))
            guard metrics != last else { return }
            last = metrics
            onChange?(metrics)
            window.contentView?.needsLayout = true
            window.contentView?.needsDisplay = true
            // A settings window can be stranded on a removed display. Leave menu
            // bar panel positioning to AppKit so its status-item anchor is preserved.
            if window.styleMask.contains(.titled) && !screen.visibleFrame.contains(window.frame) {
                window.setFrame(WindowGeometry.contained(window.frame, in: screen.visibleFrame), display: true)
            }
        }

        deinit {
            pending?.cancel()
            observers.forEach(NotificationCenter.default.removeObserver)
        }
    }
}

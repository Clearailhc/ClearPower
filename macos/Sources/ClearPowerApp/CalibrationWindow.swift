// Full-screen white surface shown while brightness is swept: maximal, known emission so
// the display's power-vs-brightness curve is measured with high SNR. Click to cancel.
import AppKit
import ClearPowerCore

final class CalibrationWindow {
    private var window: NSWindow?
    private var label: NSTextField?
    var onCancel: (() -> Void)?

    func show() {
        guard window == nil, let screen = NSScreen.main else { return }
        let w = ClickWindow(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        w.level = .screenSaver
        w.backgroundColor = .white
        w.isOpaque = true
        w.ignoresMouseEvents = false
        w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        w.onClick = { [weak self] in self?.onCancel?() }
        let l = NSTextField(labelWithString: "")
        l.alignment = .center
        l.textColor = NSColor(white: 0.47, alpha: 1)
        l.font = NSFont.systemFont(ofSize: 18)
        l.maximumNumberOfLines = 2
        l.frame = NSRect(x: 0, y: 48, width: screen.frame.width, height: 60)
        w.contentView?.addSubview(l)
        label = l
        w.makeKeyAndOrderFront(nil)
        window = w
    }

    func update(progress: Double) {
        label?.stringValue = I18n.t("calibrating", ["p": Int((progress * 100).rounded())]) + "\n" + I18n.t("calibrateHint")
    }

    func hide() {
        window?.orderOut(nil)
        window = nil
        label = nil
    }
}

private final class ClickWindow: NSWindow {
    var onClick: (() -> Void)?
    override var canBecomeKey: Bool { true }
    override func mouseDown(with event: NSEvent) { onClick?() }
}

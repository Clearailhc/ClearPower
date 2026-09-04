// Tiny in-app dictionary, shared with the GNOME frontend (extension/clearpower@lhc/i18n.js).
// gettext would follow the system locale only; this lets the user switch language at runtime.
import Foundation

public enum I18n {
    public static let strings: [String: [String: String]] = [
        "en": [
            "limit": "Limit {n}%",
            "discharge": "Discharge",
            "dischargingTo": "Discharging → {n}%",
            "topUp": "Top Up",
            "toppingUp": "Topping up…",
            "daemonOffline": "ClearPower helper is not running",
            "adapter": "Adapter", "battery": "Battery", "system": "System",
            "cpu": "CPU", "gpu": "GPU", "soc": "SoC", "memory": "Memory", "display": "Display", "other": "Other",
            "displayOther": "Display etc.",
            "noApps": "No apps using significant energy",
            "remaining": "≈ {t} left",
            "toLimit": "≈ {t} to {n}%",
            "charging": "Charging",
            "atLimit": "Charge limit reached · on AC",
            "pluggedIn": "On AC power",
            "estimating": "Estimating…",
            "calibrating": "Calibrating display… {p}%",
            "calibrateHint": "Keep the machine idle · click to cancel",
            "win10": "10 min", "win30": "30 min", "win60": "1 h",
            "health": "Health {p}% · {full} of {design} Wh · {n} cycles",
            "prefsCharge": "Charging",
            "prefsLimit": "Charge limit",
            "prefsLimitSub": "Charging stops at this level (50–100 %). The popover button cycles 80 / 90 / 100.",
            "prefsShowIcon": "Show icon in the menu bar",
            "errPermission": "Not permitted",
            "errUnsupported": "Not supported on this machine",
            "prefsTopBar": "Menu bar",
            "prefsPanelText": "Text next to the icon",
            "panelWatts": "System power (W)", "panelPercent": "Battery %", "panelBoth": "Both",
            "panelRuntime": "Time remaining", "panelNone": "None",
            "prefsFlow": "Flow animation",
            "prefsFlowSub": "Gentle sheen on the power-flow diagram while the popover is open",
            "flowAlways": "Always", "flowOnAc": "Only on AC power", "flowNever": "Never",
            "prefsRuntime": "Runtime estimate",
            "prefsWindow": "Averaging window",
            "prefsWindowSub": "Battery drain is averaged over this window",
            "prefsDisplay": "Display power",
            "prefsContent": "Content-aware estimate",
            "prefsContentSub": "While the popover is open, a tiny thumbnail of the screen is sampled every few seconds to track average brightness (needed for OLED). Nothing is stored. Requires the Screen Recording permission.",
            "prefsCalibrate": "Calibrate",
            "prefsCalibrateTitle": "Calibrate display power",
            "prefsCalibrateSub": "The screen turns white and brightness is swept for about 45 s while platform power is measured. Keep the machine idle.",
            "calibratedOn": "Calibrated {d}",
            "notCalibrated": "Not calibrated — the display is shown together with other peripherals",
            "calibFailed": "Calibration failed: {m}",
            "prefsLanguage": "Language",
            "langSystem": "System", "langEn": "English", "langZh": "中文",
            // macOS additions
            "powerLow": "Low Power", "powerAuto": "Automatic", "powerHigh": "High Power",
            "prefsHelper": "Charge control helper",
            "helperInstalled": "Helper {v} installed",
            "helperMissing": "Not installed — charge control is unavailable",
            "helperInstall": "Install…", "helperRemove": "Remove…",
            "helperExplain": "ClearPower needs a small privileged helper to control charging (it writes the SMC keys that stop and start charging). macOS will ask for an administrator password once.",
            "helperUpdateNeeded": "Helper {v} is outdated — reinstall to match this app",
            "launchAtLogin": "Launch at login",
            "settings": "Settings…", "quit": "Quit ClearPower",
            "installFailed": "Helper installation failed: {m}",
            "sleepNote": "Charging is enforced by the helper while the Mac is awake; before sleep it stops charging once the battery is within 5 % of the limit.",
            "aboutVersion": "ClearPower {v}",
            // node tooltips
            "tipAdapter": "Power drawn from the charger",
            "tipBattery": "Power supplied by the battery",
            "tipBatchg": "Power going into the battery",
            "tipSystem": "Whole-machine draw (measured)",
            "tipCpu": "Processor cores",
            "tipGpu": "Graphics processor",
            "tipSoc": "Rest of the chip: neural engine, media engines, memory controllers, display engines",
            "tipMemory": "DRAM",
            "tipDisplay": "Panel emission, from calibration × brightness (estimate)",
            "tipOther": "Everything without a sensor: SSD, Wi-Fi, USB, panel electronics — and the display before calibration",
            "tipDisplayOther": "Display and everything without a sensor: SSD, Wi-Fi, USB — calibrate the display to split them",
        ],
        "zh_CN": [
            "limit": "上限 {n}%",
            "discharge": "放电",
            "dischargingTo": "放电至 {n}%",
            "topUp": "补满",
            "toppingUp": "补满中…",
            "daemonOffline": "ClearPower 助手未运行",
            "adapter": "适配器", "battery": "电池", "system": "整机",
            "cpu": "CPU", "gpu": "GPU", "soc": "SoC", "memory": "内存", "display": "屏幕", "other": "其他",
            "displayOther": "屏幕+外围",
            "noApps": "没有应用在显著耗电",
            "remaining": "预计还能用 {t}",
            "toLimit": "预计 {t} 充到 {n}%",
            "charging": "充电中",
            "atLimit": "已到上限 · 外接供电",
            "pluggedIn": "外接供电",
            "estimating": "估算中…",
            "calibrating": "正在校准屏幕… {p}%",
            "calibrateHint": "请保持机器空闲 · 点击可取消",
            "win10": "10 分钟", "win30": "30 分钟", "win60": "1 小时",
            "health": "健康 {p}% · {full} / {design} Wh · 循环 {n} 次",
            "prefsCharge": "充电",
            "prefsLimit": "充电上限",
            "prefsLimitSub": "充到此电量停止（50–100%）。弹窗里的按钮在 80 / 90 / 100 之间循环。",
            "prefsShowIcon": "在菜单栏显示图标",
            "errPermission": "没有权限",
            "errUnsupported": "此机器不支持",
            "prefsTopBar": "菜单栏",
            "prefsPanelText": "图标旁显示",
            "panelWatts": "整机功耗 (W)", "panelPercent": "电量 %", "panelBoth": "两者",
            "panelRuntime": "剩余时间", "panelNone": "不显示",
            "prefsFlow": "流动动画",
            "prefsFlowSub": "弹窗打开时，功耗流向图上缓慢流动的光泽",
            "flowAlways": "始终", "flowOnAc": "仅接电时", "flowNever": "从不",
            "prefsRuntime": "续航估计",
            "prefsWindow": "平均窗口",
            "prefsWindowSub": "按此窗口内电池能量的变化计算平均功耗",
            "prefsDisplay": "屏幕功耗",
            "prefsContent": "按画面内容修正",
            "prefsContentSub": "弹窗打开时，每几秒取一张极小的屏幕缩略图计算平均亮度（OLED 需要）。不保存任何内容。需要「屏幕录制」权限。",
            "prefsCalibrate": "校准",
            "prefsCalibrateTitle": "校准屏幕功耗",
            "prefsCalibrateSub": "屏幕将变为全白并在约 45 秒内扫描亮度，同时测量整机功耗，期间请保持机器空闲。",
            "calibratedOn": "已校准：{d}",
            "notCalibrated": "未校准——屏幕与其他外围合并显示",
            "calibFailed": "校准失败：{m}",
            "prefsLanguage": "语言",
            "langSystem": "跟随系统", "langEn": "English", "langZh": "中文",
            "powerLow": "低电量模式", "powerAuto": "自动", "powerHigh": "高性能",
            "prefsHelper": "充电控制助手",
            "helperInstalled": "已安装助手 {v}",
            "helperMissing": "未安装——无法控制充电",
            "helperInstall": "安装…", "helperRemove": "移除…",
            "helperExplain": "ClearPower 需要一个小型特权助手来控制充电（写入停止/恢复充电的 SMC 键）。macOS 会要求输入一次管理员密码。",
            "helperUpdateNeeded": "助手 {v} 已过时——请重新安装以匹配本应用",
            "launchAtLogin": "登录时启动",
            "settings": "设置…", "quit": "退出 ClearPower",
            "installFailed": "助手安装失败：{m}",
            "sleepNote": "Mac 唤醒状态下由助手执行充电上限；进入睡眠前，若电量已在上限 5% 以内则先停止充电。",
            "aboutVersion": "ClearPower {v}",
            "tipAdapter": "从充电器取的功率",
            "tipBattery": "电池输出的功率",
            "tipBatchg": "充进电池的功率",
            "tipSystem": "整机功耗（实测）",
            "tipCpu": "处理器核心",
            "tipGpu": "图形处理器",
            "tipSoc": "芯片其余部分：神经网络引擎、媒体引擎、内存控制器、显示引擎",
            "tipMemory": "内存 (DRAM)",
            "tipDisplay": "屏幕发光功率，按校准表 × 亮度估算",
            "tipOther": "没有传感器的部分：SSD、Wi-Fi、USB、屏幕电路——校准前也包含屏幕",
            "tipDisplayOther": "屏幕和没有传感器的部分：SSD、Wi-Fi、USB——校准屏幕后可拆开",
        ],
    ]

    public private(set) static var current = "en"

    /// pref: "system" | "en" | "zh-cn". `systemLanguages` are BCP-47 codes, most preferred first.
    public static func resolveLanguage(_ pref: String, systemLanguages: [String]) -> String {
        if pref == "zh-cn" { return "zh_CN" }
        if pref == "en" { return "en" }
        return systemLanguages.contains { $0.lowercased().hasPrefix("zh") } ? "zh_CN" : "en"
    }

    public static func setLanguage(_ pref: String, systemLanguages: [String] = Locale.preferredLanguages) {
        current = resolveLanguage(pref, systemLanguages: systemLanguages)
    }

    public static func t(_ key: String, _ vars: [String: Any] = [:]) -> String {
        var s = strings[current]?[key] ?? strings["en"]?[key] ?? key
        for (k, v) in vars {
            s = s.replacingOccurrences(of: "{\(k)}", with: "\(v)")
        }
        return s
    }

    public static func fmtDuration(minutes: Double) -> String {
        let m = max(0, Int(minutes.rounded()))
        let h = m / 60, mm = m % 60
        if current == "zh_CN" {
            return h > 0 ? "\(h) 小时 \(mm) 分" : "\(mm) 分钟"
        }
        return h > 0 ? "\(h) h \(mm) m" : "\(mm) min"
    }
}

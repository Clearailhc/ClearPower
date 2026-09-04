import GLib from 'gi://GLib';

// Tiny in-app dictionary. gettext would follow the system locale only; this lets
// the user switch language in the preferences and will be reused by other frontends.
const STRINGS = {
    en: {
        limit: 'Limit {n}%',
        discharge: 'Discharge',
        dischargingTo: 'Discharging → {n}%',
        topUp: 'Top Up',
        toppingUp: 'Topping up…',
        daemonOffline: 'ClearPower daemon is not running',
        adapter: 'Adapter', battery: 'Battery', system: 'System',
        cpu: 'CPU', gpu: 'GPU', soc: 'SoC', memory: 'Memory', display: 'Display', other: 'Other',
        displayOther: 'Display etc.',
        noApps: 'No apps using significant energy',
        remaining: '≈ {t} left',
        toLimit: '≈ {t} to {n}%',
        charging: 'Charging',
        atLimit: 'Charge limit reached · on AC',
        pluggedIn: 'On AC power',
        estimating: 'Estimating…',
        calibrating: 'Calibrating display… {p}%',
        calibrateHint: 'Keep the machine idle · click to cancel',
        win10: '10 min', win30: '30 min', win60: '1 h',
        health: 'Health {p}% · {full} of {design} Wh · {n} cycles',
        prefsCharge: 'Charging',
        prefsLimit: 'Charge limit',
        prefsLimitSub: 'Charging stops at this level (50–100 %). The popover button cycles 80 / 90 / 100.',
        prefsShowIcon: 'Show icon in the top bar',
        errPermission: 'Not permitted',
        errUnsupported: 'Not supported on this machine',
        // preferences
        prefsTopBar: 'Top bar',
        prefsPanelText: 'Text next to the icon',
        panelWatts: 'System power (W)', panelPercent: 'Battery %', panelBoth: 'Both',
        panelRuntime: 'Time remaining', panelNone: 'None',
        prefsFlow: 'Flow animation',
        prefsFlowSub: 'Gentle sheen on the power-flow diagram while the popover is open',
        flowAlways: 'Always', flowOnAc: 'Only on AC power', flowNever: 'Never',
        prefsRuntime: 'Runtime estimate',
        prefsWindow: 'Averaging window',
        prefsWindowSub: 'Battery drain is averaged over this window',
        prefsDisplay: 'Display power',
        prefsContent: 'Content-aware estimate',
        prefsContentSub: 'While the popover is open, a tiny thumbnail of the screen is sampled every few seconds to track average brightness (needed for OLED). Nothing is stored.',
        prefsCalibrate: 'Calibrate',
        prefsCalibrateTitle: 'Calibrate display power',
        prefsCalibrateSub: 'The screen turns white and brightness is swept for about 45 s while platform power is measured. Keep the machine idle.',
        calibratedOn: 'Calibrated {d}',
        notCalibrated: 'Not calibrated — the display is shown together with other peripherals',
        calibFailed: 'Calibration failed: {m}',
        prefsLanguage: 'Language',
        langSystem: 'System', langEn: 'English', langZh: '中文',
    },
    zh_CN: {
        limit: '上限 {n}%',
        discharge: '放电',
        dischargingTo: '放电至 {n}%',
        topUp: '补满',
        toppingUp: '补满中…',
        daemonOffline: 'ClearPower 守护进程未运行',
        adapter: '适配器', battery: '电池', system: '整机',
        cpu: 'CPU', gpu: 'GPU', soc: 'SoC', memory: '内存', display: '屏幕', other: '其他',
        displayOther: '屏幕+外围',
        noApps: '没有应用在显著耗电',
        remaining: '预计还能用 {t}',
        toLimit: '预计 {t} 充到 {n}%',
        charging: '充电中',
        atLimit: '已到上限 · 外接供电',
        pluggedIn: '外接供电',
        estimating: '估算中…',
        calibrating: '正在校准屏幕… {p}%',
        calibrateHint: '请保持机器空闲 · 点击可取消',
        win10: '10 分钟', win30: '30 分钟', win60: '1 小时',
        health: '健康 {p}% · {full} / {design} Wh · 循环 {n} 次',
        prefsCharge: '充电',
        prefsLimit: '充电上限',
        prefsLimitSub: '充到此电量停止（50–100%）。弹窗里的按钮在 80 / 90 / 100 之间循环。',
        prefsShowIcon: '在顶栏显示图标',
        errPermission: '没有权限',
        errUnsupported: '此机器不支持',
        prefsTopBar: '顶栏',
        prefsPanelText: '图标旁显示',
        panelWatts: '整机功耗 (W)', panelPercent: '电量 %', panelBoth: '两者',
        panelRuntime: '剩余时间', panelNone: '不显示',
        prefsFlow: '流动动画',
        prefsFlowSub: '弹窗打开时，功耗流向图上缓慢流动的光泽',
        flowAlways: '始终', flowOnAc: '仅接电时', flowNever: '从不',
        prefsRuntime: '续航估计',
        prefsWindow: '平均窗口',
        prefsWindowSub: '按此窗口内电池能量的变化计算平均功耗',
        prefsDisplay: '屏幕功耗',
        prefsContent: '按画面内容修正',
        prefsContentSub: '弹窗打开时，每几秒取一张极小的屏幕缩略图计算平均亮度（OLED 需要）。不保存任何内容。',
        prefsCalibrate: '校准',
        prefsCalibrateTitle: '校准屏幕功耗',
        prefsCalibrateSub: '屏幕将变为全白并在约 45 秒内扫描亮度，同时测量整机功耗，期间请保持机器空闲。',
        calibratedOn: '已校准：{d}',
        notCalibrated: '未校准——屏幕与其他外围合并显示',
        calibFailed: '校准失败：{m}',
        prefsLanguage: '语言',
        langSystem: '跟随系统', langEn: 'English', langZh: '中文',
    },
};

let current = 'en';

export function resolveLanguage(pref) {
    if (pref === 'zh-cn')
        return 'zh_CN';
    if (pref === 'en')
        return 'en';
    return GLib.get_language_names().some(n => n.toLowerCase().startsWith('zh')) ? 'zh_CN' : 'en';
}

export function setLanguage(pref) {
    current = resolveLanguage(pref);
}

export function language() {
    return current;
}

export function t(key, vars = null) {
    let s = STRINGS[current]?.[key] ?? STRINGS.en[key] ?? key;
    if (vars)
        for (const [k, v] of Object.entries(vars))
            s = s.replace(`{${k}}`, String(v));
    return s;
}

export function fmtDuration(minutes) {
    const m = Math.max(0, Math.round(minutes));
    const h = Math.floor(m / 60), mm = m % 60;
    if (current === 'zh_CN')
        return h > 0 ? `${h} 小时 ${mm} 分` : `${mm} 分钟`;
    return h > 0 ? `${h} h ${mm} m` : `${mm} min`;
}

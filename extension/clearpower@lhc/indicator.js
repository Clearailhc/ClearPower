import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import GObject from 'gi://GObject';
import St from 'gi://St';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';

import {BatteryBar} from './batteryBar.js';
import {Sankey, fmtW} from './sankey.js';
import {PROFILES} from './powerProfiles.js';
import {t, fmtDuration, setLanguage} from './i18n.js';
import {sampleAverageLuminance} from './content.js';
import {CalibrationScreen} from './calibrationScreen.js';

const LIMITS = [80, 90, 100];      // one click cycles through these
const WINDOWS = [10, 30, 60];      // runtime averaging windows (minutes)
const APP_MIN_W = 0.5;
const CONTENT_INTERVAL_S = 5;

export const Indicator = GObject.registerClass(
class Indicator extends PanelMenu.Button {
    _init(ext, client, profiles, settings) {
        super._init(0.5, 'ClearPower');
        this._ext = ext;
        this._client = client;
        this._profiles = profiles;
        this._settings = settings;
        this._appsTimer = 0;
        this._contentTimer = 0;
        this._calScreen = new CalibrationScreen(() => this._client.cancelCalibration().catch(e => this._fail(e)));
        setLanguage(settings.get_string('language'));

        const box = new St.BoxLayout({style_class: 'panel-status-menu-box'});
        this._gicon = Gio.icon_new_for_string(`${ext.path}/icons/clearpower-symbolic.svg`);
        this._icon = new St.Icon({gicon: this._gicon, style_class: 'system-status-icon'});
        this._label = new St.Label({text: '', y_align: Clutter.ActorAlign.CENTER, style_class: 'clearpower-panel-label'});
        box.add_child(this._icon);
        box.add_child(this._label);
        this.add_child(box);

        this._buildMenu();

        this._clientIds = [
            client.connect('sample', (_c, s) => this._onSample(s)),
            client.connect('state', () => this._syncState()),
            client.connect('online', () => this._syncOnline()),
        ];
        this._profId = profiles.connect('changed', () => this._syncProfiles());
        this._settingsIds = [
            settings.connect('changed::panel-text', () => this._updatePanel()),
            settings.connect('changed::runtime-window', () => this._refreshRuntime()),
            settings.connect('changed::flow-animation', () => this._sankey.setFlowMode(settings.get_string('flow-animation'))),
            settings.connect('changed::content-aware', () => this._syncContentTimer()),
            settings.connect('changed::language', () => {
                setLanguage(settings.get_string('language'));
                this._retext();
            }),
        ];
        this._sankey.setFlowMode(settings.get_string('flow-animation'));
        this.menu.connect('open-state-changed', (_m, open) => {
            this._sankey.setActive(open);
            if (open) {
                this._refreshAll();
                this._pollApps();
                this._appsTimer = GLib.timeout_add_seconds(GLib.PRIORITY_DEFAULT, 3, () => {
                    this._pollApps();
                    return GLib.SOURCE_CONTINUE;
                });
            } else if (this._appsTimer) {
                GLib.source_remove(this._appsTimer);
                this._appsTimer = 0;
            }
            this._syncContentTimer();
        });
        this._syncOnline();
        this._syncState();
        this._syncProfiles();
        this._updatePanel();
    }

    _row(actor) {
        const item = new PopupMenu.PopupBaseMenuItem({reactive: false, can_focus: false});
        item.add_child(actor);
        this.menu.addMenuItem(item);
        return item;
    }

    _button(text, iconName, cb, styleClass = 'button clearpower-btn') {
        const content = new St.BoxLayout({style_class: 'clearpower-row'});
        content.add_child(new St.Label({text, y_align: Clutter.ActorAlign.CENTER}));
        if (iconName)
            content.add_child(new St.Icon({icon_name: iconName, icon_size: 16, y_align: Clutter.ActorAlign.CENTER}));
        const b = new St.Button({style_class: styleClass, child: content});
        b._label = content.get_first_child();
        b.connect('clicked', cb);
        return b;
    }

    _buildMenu() {
        this.menu.box.add_style_class_name('clearpower-menu');

        this._offline = new St.Label({
            text: t('daemonOffline'),
            style_class: 'clearpower-error', x_expand: true, x_align: Clutter.ActorAlign.CENTER,
        });
        this._offlineItem = this._row(this._offline);

        // Header: limit (click cycles 80/90/100), discharge, top-up, settings
        const header = new St.BoxLayout({style_class: 'clearpower-row', x_expand: true});
        this._limitBtn = this._button(t('limit', {n: '–'}), null, () => this._cycleLimit(), 'button clearpower-pill');
        header.add_child(this._limitBtn);
        header.add_child(new St.Widget({x_expand: true}));
        this._dischargeBtn = this._button(t('discharge'), 'list-remove-symbolic', () => this._toggleDischarge());
        this._topupBtn = this._button(t('topUp'), 'list-add-symbolic', () => this._toggleTopUp());
        this._prefsBtn = new St.Button({
            style_class: 'button clearpower-icon-btn',
            child: new St.Icon({icon_name: 'emblem-system-symbolic', icon_size: 16}),
        });
        this._prefsBtn.connect('clicked', () => {
            this.menu.close();
            this._ext.openPreferences();
        });
        header.add_child(this._dischargeBtn);
        header.add_child(this._topupBtn);
        header.add_child(this._prefsBtn);
        this._headerItem = this._row(header);

        // Battery bar + runtime line
        const batBox = new St.BoxLayout({vertical: true, x_expand: true, style_class: 'clearpower-row'});
        this._bar = new BatteryBar();
        batBox.add_child(this._bar);
        const rt = new St.BoxLayout({style_class: 'clearpower-row', x_expand: true});
        this._runtime = new St.Label({text: '', style_class: 'clearpower-dim', y_align: Clutter.ActorAlign.CENTER, x_expand: true});
        this._windowBtn = this._button('', null, () => this._cycleWindow(), 'button clearpower-mini');
        rt.add_child(this._runtime);
        rt.add_child(this._windowBtn);
        batBox.add_child(rt);
        this._batItem = this._row(batBox);

        this._sankey = new Sankey();
        this._sankeyItem = this._row(this._sankey);

        const pbox = new St.BoxLayout({style_class: 'clearpower-row', x_expand: true});
        this._profileBtns = {};
        for (const p of PROFILES) {
            const b = new St.Button({
                style_class: 'button clearpower-icon-btn',
                child: new St.Icon({icon_name: p.icon, icon_size: 16}),
            });
            b.connect('clicked', () => this._profiles.set(p.id));
            pbox.add_child(b);
            this._profileBtns[p.id] = b;
        }
        pbox.add_child(new St.Widget({x_expand: true}));
        this._temps = new St.Label({style_class: 'clearpower-dim', y_align: Clutter.ActorAlign.CENTER});
        pbox.add_child(this._temps);
        this._profItem = this._row(pbox);

        this._apps = new St.BoxLayout({vertical: true, x_expand: true, style_class: 'clearpower-apps'});
        this._appsItem = this._row(this._apps);
        this._windowBtn._label.text = this._windowText();
    }

    _retext() {
        this._offline.text = t('daemonOffline');
        this._syncState();
        this._windowBtn._label.text = this._windowText();
        this._sankey.invalidate();
        this._refreshAll();
        this._pollApps();
        this._updatePanel();
    }

    // ---- charge control -------------------------------------------------
    _cycleLimit() {
        const cur = this._client.limit;
        const i = LIMITS.indexOf(cur);
        const next = LIMITS[(i + 1) % LIMITS.length] ?? LIMITS[0];
        this._limitBtn._label.text = t('limit', {n: next});
        this._client.setChargeLimit(next).catch(e => this._fail(e));
    }

    _toggleDischarge() {
        const p = this._client.mode === 'discharge'
            ? this._client.cancelSpecial() : this._client.startDischarge(0);
        p.catch(e => this._fail(e));
    }

    _toggleTopUp() {
        const p = this._client.mode === 'topup'
            ? this._client.cancelSpecial() : this._client.startTopUp();
        p.catch(e => this._fail(e));
    }

    _fail(e) {
        console.error(`ClearPower: ${e.message}`);
        let msg = e.message.replace(/^GDBus.Error:[^:]+: /, '');
        if (/NotAuthorized|Permission denied/i.test(e.message))
            msg = t('errPermission');
        else if (/not supported/i.test(e.message))
            msg = t('errUnsupported');
        Main.notify('ClearPower', msg);
        this._syncState();
    }

    // ---- runtime window -----------------------------------------------------
    _window() {
        const w = this._settings.get_int('runtime-window');
        return WINDOWS.includes(w) ? w : 30;
    }

    _windowText() {
        return t(`win${this._window()}`);
    }

    _cycleWindow() {
        const i = WINDOWS.indexOf(this._window());
        this._settings.set_int('runtime-window', WINDOWS[(i + 1) % WINDOWS.length]);
        this._windowBtn._label.text = this._windowText();
    }

    _refreshRuntime(snap = this._client.snapshot) {
        this._windowBtn._label.text = this._windowText();
        if (!snap)
            return;
        const w = this._window();
        let text = '';
        if (snap.calib_state === 'running') {
            text = t('calibrating', {p: Math.round((snap.calib_progress ?? 0) * 100)});
        } else if (snap.bat_status === 'Discharging') {
            const m = snap[`runtime_min_${w}`] ?? -1;
            text = m > 0 ? ((snap.runtime_basis_s ?? 0) < 300 ? '~' : '') + t('remaining', {t: fmtDuration(m)}) : t('estimating');
        } else if (snap.bat_status === 'Charging') {
            const m = snap[`eta_min_${w}`] ?? -1;
            text = m > 0 ? t('toLimit', {t: fmtDuration(m), n: this._client.limit}) : t('charging');
        } else if (snap.on_ac) {
            text = (snap.bat_pct ?? 0) >= this._client.limit - 1 ? t('atLimit') : t('pluggedIn');
        }
        this._runtime.text = text;
        this._windowBtn.visible = snap.bat_status === 'Discharging' || snap.bat_status === 'Charging';
    }

    // ---- screen content sampling (OLED display estimate) --------------------
    _contentWanted() {
        if (!this._client.online || !this._settings.get_boolean('content-aware'))
            return false;
        return this.menu.isOpen || this._client.snapshot?.calib_state === 'running';
    }

    _syncContentTimer() {
        const want = this._contentWanted();
        if (want && !this._contentTimer) {
            this._sampleContent();
            this._contentTimer = GLib.timeout_add_seconds(GLib.PRIORITY_LOW, CONTENT_INTERVAL_S, () => {
                this._sampleContent();
                return GLib.SOURCE_CONTINUE;
            });
        } else if (!want && this._contentTimer) {
            GLib.source_remove(this._contentTimer);
            this._contentTimer = 0;
        }
    }

    _sampleContent() {
        sampleAverageLuminance()
            .then(apl => (apl >= 0 ? this._client.setDisplayContent(apl) : null))
            .catch(e => console.error(`ClearPower: content sample: ${e.message}`));
    }

    // ---- sync -------------------------------------------------------------
    _syncOnline() {
        const on = this._client.online;
        this._offlineItem.visible = !on;
        for (const it of [this._headerItem, this._batItem, this._sankeyItem, this._profItem, this._appsItem])
            it.visible = on;
        if (on)
            this._syncState();
        this._updatePanel();
        this._syncContentTimer();
    }

    _syncState() {
        const c = this._client;
        this._limitBtn._label.text = t('limit', {n: c.limit});
        this._limitBtn.reactive = c.mode === 'limit' && c.controlSupported;
        this._limitBtn.opacity = this._limitBtn.reactive ? 255 : 150;
        this._bar.update({limit: c.limit, mode: c.mode});
        this._batItem.visible = c.online;
        this._dischargeBtn.visible = c.dischargeSupported;
        const setChecked = (b, on) => on ? b.add_style_pseudo_class('checked') : b.remove_style_pseudo_class('checked');
        setChecked(this._dischargeBtn, c.mode === 'discharge');
        setChecked(this._topupBtn, c.mode === 'topup');
        this._dischargeBtn._label.text = c.mode === 'discharge' ? t('dischargingTo', {n: c.target}) : t('discharge');
        this._topupBtn._label.text = c.mode === 'topup' ? t('toppingUp') : t('topUp');
        this._refreshRuntime();
    }

    _syncProfiles() {
        const active = this._profiles.active;
        for (const [id, b] of Object.entries(this._profileBtns)) {
            b.visible = this._profiles.available.length === 0 || this._profiles.available.includes(id);
            if (id === active)
                b.add_style_pseudo_class('checked');
            else
                b.remove_style_pseudo_class('checked');
        }
    }

    _onSample(snap) {
        this._updatePanel();
        if (this.menu.isOpen)
            this._refreshAll(snap);
        const running = snap.calib_state === 'running';
        if (running) {
            if (this.menu.isOpen)
                this.menu.close();
            this._calScreen.show();
            this._calScreen.update(snap.calib_progress ?? 0);
        } else {
            this._calScreen.hide();
        }
        if (running !== !!this._contentTimer)
            this._syncContentTimer();
    }

    _refreshAll(snap = this._client.snapshot) {
        if (!snap)
            return;
        this._bar.update({pct: snap.bat_pct ?? 0, status: snap.bat_status ?? '', onAc: !!snap.on_ac});
        this._sankey.update(snap);
        this._refreshRuntime(snap);
        const parts = [];
        if (snap.temp_cpu >= 0)
            parts.push(`CPU ${Math.round(snap.temp_cpu)}°`);
        if (snap.temp_gpu >= 0)
            parts.push(`GPU ${Math.round(snap.temp_gpu)}°`);
        if (snap.temp_nvme >= 0)
            parts.push(`SSD ${Math.round(snap.temp_nvme)}°`);
        if (snap.fan1 > 0)
            parts.push(`${snap.fan1} rpm`);
        this._temps.text = parts.join(' · ');
    }

    _pollApps() {
        if (!this._client.online)
            return;
        this._client.getTopProcesses(3)
            .then(procs => this._refreshApps(procs))
            .catch(e => console.error(`ClearPower: GetTopProcesses: ${e.message}`));
    }

    _refreshApps(procs) {
        this._apps.destroy_all_children();
        const sig = procs.filter(([, w]) => w >= APP_MIN_W);
        if (sig.length === 0) {
            this._apps.add_child(new St.Label({
                text: t('noApps'), x_expand: true,
                x_align: Clutter.ActorAlign.CENTER, style_class: 'clearpower-dim',
            }));
            return;
        }
        for (const [name, w] of sig) {
            const row = new St.BoxLayout({style_class: 'clearpower-app-row', x_expand: true});
            row.add_child(new St.Label({text: name, x_expand: true}));
            row.add_child(new St.Label({text: fmtW(w), style_class: 'clearpower-dim'}));
            this._apps.add_child(row);
        }
    }

    _updatePanel() {
        const snap = this._client.snapshot;
        if (!this._client.online || !snap) {
            this._icon.opacity = 120;
            if (this._label.text !== '') {
                this._label.text = '';
                this._label.visible = false;
            }
            return;
        }
        this._icon.opacity = 255;
        const mode = this._settings.get_string('panel-text');
        const w = fmtW(snap.sys_w, 1);
        const p = `${snap.bat_pct ?? 0}%`;
        let rt = p;
        if (snap.bat_status === 'Discharging') {
            const m = snap[`runtime_min_${this._window()}`] ?? -1;
            if (m > 0)
                rt = fmtDuration(m);
        }
        const text = {watts: w, percent: p, both: `${w} · ${p}`, runtime: rt, none: ''}[mode] ?? w;
        if (this._label.text !== text) {
            this._label.text = text;
            this._label.visible = text !== '';
        }
    }

    destroy() {
        this._calScreen.hide();
        if (this._appsTimer)
            GLib.source_remove(this._appsTimer);
        if (this._contentTimer)
            GLib.source_remove(this._contentTimer);
        for (const id of this._clientIds)
            this._client.disconnect(id);
        this._profiles.disconnect(this._profId);
        for (const id of this._settingsIds)
            this._settings.disconnect(id);
        super.destroy();
    }
});

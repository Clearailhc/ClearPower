import Clutter from 'gi://Clutter';
import GLib from 'gi://GLib';
import GObject from 'gi://GObject';
import St from 'gi://St';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';
import {Slider} from 'resource:///org/gnome/shell/ui/slider.js';

import {BatteryBar} from './batteryBar.js';
import {Sankey, fmtW} from './sankey.js';
import {PROFILES} from './powerProfiles.js';

const LIMIT_MIN = 50;
const APP_MIN_W = 0.5;
const limitToSlider = l => (l - LIMIT_MIN) / (100 - LIMIT_MIN);
const sliderToLimit = v => Math.round((LIMIT_MIN + v * (100 - LIMIT_MIN)) / 5) * 5;

export const Indicator = GObject.registerClass(
class Indicator extends PanelMenu.Button {
    _init(ext, client, profiles, settings) {
        super._init(0.5, 'ClearPower');
        this._ext = ext;
        this._client = client;
        this._profiles = profiles;
        this._settings = settings;
        this._dragging = false;
        this._commitTimer = 0;

        const box = new St.BoxLayout({style_class: 'panel-status-menu-box'});
        this._icon = new St.Icon({icon_name: 'battery-missing-symbolic', style_class: 'system-status-icon'});
        this._label = new St.Label({text: '–', y_align: Clutter.ActorAlign.CENTER, style_class: 'clearpower-panel-label'});
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
        this._settingsId = settings.connect('changed::panel-text', () => this._updatePanel());
        this._flowId = settings.connect('changed::flow-animation',
            () => this._sankey.setFlowMode(settings.get_string('flow-animation')));
        this._sankey.setFlowMode(settings.get_string('flow-animation'));
        this._appsTimer = 0;
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

    _button(text, iconName, cb) {
        const content = new St.BoxLayout({style_class: 'clearpower-row'});
        content.add_child(new St.Label({text, y_align: Clutter.ActorAlign.CENTER}));
        content.add_child(new St.Icon({icon_name: iconName, icon_size: 16, y_align: Clutter.ActorAlign.CENTER}));
        const b = new St.Button({style_class: 'button clearpower-btn', child: content});
        b._label = content.get_first_child();
        b.connect('clicked', cb);
        return b;
    }

    _buildMenu() {
        this.menu.box.add_style_class_name('clearpower-menu');

        this._offline = new St.Label({
            text: 'ClearPower daemon is not running',
            style_class: 'clearpower-error', x_expand: true, x_align: Clutter.ActorAlign.CENTER,
        });
        this._offlineItem = this._row(this._offline);

        const header = new St.BoxLayout({style_class: 'clearpower-row', x_expand: true});
        this._pill = new St.Label({text: 'Limit: –', style_class: 'clearpower-pill', y_align: Clutter.ActorAlign.CENTER});
        header.add_child(this._pill);
        header.add_child(new St.Widget({x_expand: true}));
        this._dischargeBtn = this._button('Discharge', 'list-remove-symbolic', () => this._toggleDischarge());
        this._topupBtn = this._button('Top Up', 'list-add-symbolic', () => this._toggleTopUp());
        this._prefsBtn = new St.Button({
            style_class: 'button clearpower-icon-btn',
            child: new St.Icon({icon_name: 'view-grid-symbolic', icon_size: 16}),
        });
        this._prefsBtn.connect('clicked', () => {
            this.menu.close();
            this._ext.openPreferences();
        });
        header.add_child(this._dischargeBtn);
        header.add_child(this._topupBtn);
        header.add_child(this._prefsBtn);
        this._headerItem = this._row(header);

        const batBox = new St.BoxLayout({vertical: true, x_expand: true, style_class: 'clearpower-row'});
        this._bar = new BatteryBar();
        batBox.add_child(this._bar);
        this._slider = new Slider(limitToSlider(this._client.limit));
        for (let l = LIMIT_MIN; l <= 100; l += 5)
            this._slider.addMark(limitToSlider(l));
        this._slider.connect('notify::value', () => this._onSliderValue());
        this._slider.connect('drag-begin', () => {
            this._dragging = true;
        });
        this._slider.connect('drag-end', () => {
            this._dragging = false;
            this._commitLimit();
        });
        batBox.add_child(this._slider);
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
    }

    // ---- charge control -------------------------------------------------
    _onSliderValue() {
        const l = sliderToLimit(this._slider.value);
        this._pill.text = `Limit: ${l}%`;
        this._bar.update({limit: l});
        if (this._dragging)
            return;
        if (this._commitTimer)
            GLib.source_remove(this._commitTimer);
        this._commitTimer = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 500, () => {
            this._commitTimer = 0;
            this._commitLimit();
            return GLib.SOURCE_REMOVE;
        });
    }

    _commitLimit() {
        const l = sliderToLimit(this._slider.value);
        if (l === this._client.limit)
            return;
        this._client.setChargeLimit(l).catch(e => this._fail(e));
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
        Main.notify('ClearPower', e.message.replace(/^GDBus.Error:[^:]+: /, ''));
        this._syncState();
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
    }

    _syncState() {
        const c = this._client;
        if (!this._dragging && !this._commitTimer)
            this._slider.value = limitToSlider(c.limit);
        this._pill.text = `Limit: ${c.limit}%`;
        this._bar.update({limit: c.limit, mode: c.mode});
        this._batItem.visible = c.online && c.controlSupported;
        this._dischargeBtn.visible = c.dischargeSupported;
        const setChecked = (b, on) => on ? b.add_style_pseudo_class('checked') : b.remove_style_pseudo_class('checked');
        setChecked(this._dischargeBtn, c.mode === 'discharge');
        setChecked(this._topupBtn, c.mode === 'topup');
        this._dischargeBtn._label.text = c.mode === 'discharge' ? `Discharging → ${c.target}%` : 'Discharge';
        this._topupBtn._label.text = c.mode === 'topup' ? 'Topping up…' : 'Top Up';
        this._slider.reactive = c.mode === 'limit';
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
    }

    _refreshAll(snap = this._client.snapshot) {
        if (!snap)
            return;
        this._bar.update({pct: snap.bat_pct ?? 0, status: snap.bat_status ?? '', onAc: !!snap.on_ac});
        this._sankey.update(snap);
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
        this._client.getTopProcesses(3)
            .then(procs => this._refreshApps(procs))
            .catch(e => console.error(`ClearPower: GetTopProcesses: ${e.message}`));
    }

    _refreshApps(procs) {
        this._apps.destroy_all_children();
        const sig = procs.filter(([, w]) => w >= APP_MIN_W);
        if (sig.length === 0) {
            this._apps.add_child(new St.Label({
                text: 'No Apps Using Significant Energy', x_expand: true,
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
            this._icon.icon_name = 'battery-missing-symbolic';
            this._label.text = '';
            return;
        }
        const onAc = !!snap.on_ac;
        const charging = (snap.bat_w ?? 0) > 0;
        this._icon.icon_name = onAc ? (charging ? 'battery-good-charging-symbolic' : 'ac-adapter-symbolic') : 'battery-good-symbolic';
        const mode = this._settings.get_string('panel-text');
        const w = fmtW(snap.sys_w, 1);
        const p = `${snap.bat_pct ?? 0}%`;
        const text = {watts: w, percent: p, both: `${w} · ${p}`, none: ''}[mode] ?? w;
        if (this._label.text !== text) {
            this._label.text = text;
            this._label.visible = text !== '';
        }
    }

    destroy() {
        if (this._commitTimer)
            GLib.source_remove(this._commitTimer);
        if (this._appsTimer)
            GLib.source_remove(this._appsTimer);
        for (const id of this._clientIds)
            this._client.disconnect(id);
        this._profiles.disconnect(this._profId);
        this._settings.disconnect(this._settingsId);
        this._settings.disconnect(this._flowId);
        super.destroy();
    }
});

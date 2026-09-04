import Adw from 'gi://Adw';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Gtk from 'gi://Gtk';
import {ExtensionPreferences} from 'resource:///org/gnome/Shell/Extensions/js/extensions/prefs.js';

import {t, setLanguage} from './i18n.js';

const BUS_NAME = 'org.clearpower.Daemon1';
const OBJECT_PATH = '/org/clearpower/Daemon';
const IFACE = 'org.clearpower.Daemon1';

function combo(settings, key, title, subtitle, nicks, labels) {
    const row = new Adw.ComboRow({title, subtitle: subtitle ?? null, model: Gtk.StringList.new(labels)});
    row.selected = Math.max(0, nicks.indexOf(settings.get_string(key)));
    row.connect('notify::selected', () => settings.set_string(key, nicks[row.selected]));
    return row;
}

export default class ClearPowerPrefs extends ExtensionPreferences {
    fillPreferencesWindow(window) {
        const settings = this.getSettings();
        setLanguage(settings.get_string('language'));
        const build = () => {
            for (const p of this._pages ?? [])
                window.remove(p);
            this._pages = [this._buildPage(settings, window)];
            for (const p of this._pages)
                window.add(p);
        };
        settings.connect('changed::language', () => {
            setLanguage(settings.get_string('language'));
            build();
        });
        window.connect('close-request', () => this._stopPolling());
        build();
    }

    _buildPage(settings, window) {
        const page = new Adw.PreferencesPage();

        const top = new Adw.PreferencesGroup({title: t('prefsTopBar')});
        top.add(combo(settings, 'panel-text', t('prefsPanelText'), null,
            ['watts', 'percent', 'both', 'runtime', 'none'],
            [t('panelWatts'), t('panelPercent'), t('panelBoth'), t('panelRuntime'), t('panelNone')]));
        top.add(combo(settings, 'flow-animation', t('prefsFlow'), t('prefsFlowSub'),
            ['always', 'on-ac', 'never'], [t('flowAlways'), t('flowOnAc'), t('flowNever')]));
        top.add(combo(settings, 'language', t('prefsLanguage'), null,
            ['system', 'en', 'zh-cn'], [t('langSystem'), t('langEn'), t('langZh')]));
        page.add(top);

        const rt = new Adw.PreferencesGroup({title: t('prefsRuntime')});
        const wins = [10, 30, 60];
        const wrow = new Adw.ComboRow({
            title: t('prefsWindow'), subtitle: t('prefsWindowSub'),
            model: Gtk.StringList.new([t('win10'), t('win30'), t('win60')]),
        });
        wrow.selected = Math.max(0, wins.indexOf(settings.get_int('runtime-window')));
        wrow.connect('notify::selected', () => settings.set_int('runtime-window', wins[wrow.selected]));
        rt.add(wrow);
        page.add(rt);

        const disp = new Adw.PreferencesGroup({title: t('prefsDisplay')});
        const content = new Adw.SwitchRow({title: t('prefsContent'), subtitle: t('prefsContentSub')});
        settings.bind('content-aware', content, 'active', Gio.SettingsBindFlags.DEFAULT);
        disp.add(content);

        const cal = new Adw.ActionRow({title: t('prefsCalibrateTitle'), subtitle: t('prefsCalibrateSub')});
        const btn = new Gtk.Button({label: t('prefsCalibrate'), valign: Gtk.Align.CENTER});
        btn.add_css_class('suggested-action');
        cal.add_suffix(btn);
        const status = new Adw.ActionRow({title: t('notCalibrated')});
        status.add_css_class('property');
        disp.add(cal);
        disp.add(status);
        page.add(disp);

        this._proxy = null;
        try {
            this._proxy = Gio.DBusProxy.new_for_bus_sync(Gio.BusType.SYSTEM, Gio.DBusProxyFlags.NONE, null,
                BUS_NAME, OBJECT_PATH, IFACE, null);
        } catch (e) {
            status.title = e.message;
        }
        const refresh = () => {
            if (!this._proxy)
                return false;
            const v = this._proxy.get_cached_property('Snapshot');
            if (!v)
                return false;
            const snap = v.deep_unpack();
            const get = k => snap[k]?.deep_unpack?.() ?? snap[k];
            const state = get('calib_state');
            if (state === 'running') {
                status.title = t('calibrating', {p: Math.round((get('calib_progress') ?? 0) * 100)});
                btn.sensitive = false;
                return true;
            }
            btn.sensitive = true;
            if (get('display_calibrated')) {
                const at = get('calibrated_at') ?? 0;
                status.title = t('calibratedOn', {d: at > 0 ? GLib.DateTime.new_from_unix_local(at).format('%Y-%m-%d %H:%M') : '–'});
            } else {
                status.title = t('notCalibrated');
            }
            const msg = get('calib_message');
            if (msg)
                status.subtitle = t('calibFailed', {m: msg});
            return false;
        };
        btn.connect('clicked', () => {
            if (!this._proxy)
                return;
            this._proxy.call('CalibrateDisplay', null, Gio.DBusCallFlags.NONE, -1, null, (p, res) => {
                try {
                    p.call_finish(res);
                } catch (e) {
                    status.subtitle = e.message.replace(/^GDBus.Error:[^:]+: /, '');
                }
            });
            this._startPolling(refresh);
        });
        // GDBus fetches all properties on creation; poll once now and whenever they change.
        GLib.idle_add(GLib.PRIORITY_DEFAULT, () => {
            this._proxy?.call('org.freedesktop.DBus.Properties.Get',
                new GLib.Variant('(ss)', [IFACE, 'Snapshot']), Gio.DBusCallFlags.NONE, -1, null, (p, res) => {
                    try {
                        const [val] = p.call_finish(res).deep_unpack();
                        this._proxy.set_cached_property('Snapshot', val);
                    } catch (e) {
                        console.error(`ClearPower prefs: ${e.message}`);
                    }
                    refresh();
                });
            return GLib.SOURCE_REMOVE;
        });
        return page;
    }

    _startPolling(refresh) {
        this._stopPolling();
        this._poll = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 1000, () => {
            this._proxy?.call('org.freedesktop.DBus.Properties.Get',
                new GLib.Variant('(ss)', [IFACE, 'Snapshot']), Gio.DBusCallFlags.NONE, -1, null, (p, res) => {
                    try {
                        const [val] = p.call_finish(res).deep_unpack();
                        this._proxy.set_cached_property('Snapshot', val);
                    } catch (e) {
                        return;
                    }
                    if (!refresh())
                        this._stopPolling();
                });
            return GLib.SOURCE_CONTINUE;
        });
    }

    _stopPolling() {
        if (this._poll)
            GLib.source_remove(this._poll);
        this._poll = 0;
    }
}

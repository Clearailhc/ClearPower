import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';

import {DaemonClient} from './daemonProxy.js';
import {PowerProfiles} from './powerProfiles.js';
import {Indicator} from './indicator.js';

export default class ClearPowerExtension extends Extension {
    enable() {
        this._settings = this.getSettings();
        // CLEARPOWER_BUS=session lets a nested shell talk to `clearpowerd --bus session`.
        const bus = GLib.getenv('CLEARPOWER_BUS') === 'session' ? Gio.DBus.session : Gio.DBus.system;
        this._client = new DaemonClient(bus);
        this._profiles = new PowerProfiles();
        this._indicator = new Indicator(this, this._client, this._profiles, this._settings);
        Main.panel.addToStatusArea(this.uuid, this._indicator, 1, 'right');
        // Dev harness only (headless test shell): open the menu and allow Eval/screenshots.
        if (GLib.getenv('CLEARPOWER_DEV') === '1') {
            global.context.unsafe_mode = true;
            this._devTimer = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 3000, () => {
                this._devTimer = 0;
                this._indicator?.menu.open();
                return GLib.SOURCE_REMOVE;
            });
        }
    }

    disable() {
        if (this._devTimer)
            GLib.source_remove(this._devTimer);
        this._devTimer = 0;
        this._indicator?.destroy();
        this._indicator = null;
        this._client?.destroy();
        this._client = null;
        this._profiles?.destroy();
        this._profiles = null;
        this._settings = null;
    }
}

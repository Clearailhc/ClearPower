import Gio from 'gi://Gio';
import {EventEmitter} from 'resource:///org/gnome/shell/misc/signals.js';

const BUS_NAME = 'org.freedesktop.UPower.PowerProfiles';
const OBJECT_PATH = '/org/freedesktop/UPower/PowerProfiles';
const XML = `
<node>
  <interface name="org.freedesktop.UPower.PowerProfiles">
    <property name="ActiveProfile" type="s" access="readwrite"/>
    <property name="Profiles" type="aa{sv}" access="read"/>
  </interface>
</node>`;
const Proxy = Gio.DBusProxy.makeProxyWrapper(XML);

export const PROFILES = [
    {id: 'power-saver', icon: 'power-profile-power-saver-symbolic', label: 'Power Saver'},
    {id: 'balanced', icon: 'power-profile-balanced-symbolic', label: 'Balanced'},
    {id: 'performance', icon: 'power-profile-performance-symbolic', label: 'Performance'},
];

export class PowerProfiles extends EventEmitter {
    constructor() {
        super();
        this.active = null;
        this.available = [];
        this._proxy = new Proxy(Gio.DBus.system, BUS_NAME, OBJECT_PATH, (proxy, error) => {
            if (error) {
                console.error(`ClearPower: power-profiles proxy failed: ${error.message}`);
                return;
            }
            this._id = proxy.connect('g-properties-changed', () => this._sync());
            this._sync();
        });
    }

    _sync() {
        this.active = this._proxy.ActiveProfile ?? null;
        const list = this._proxy.Profiles ?? [];
        this.available = list.map(p => p.Profile?.unpack?.() ?? p.Profile).filter(Boolean);
        this.emit('changed');
    }

    set(profile) {
        if (this._proxy)
            this._proxy.ActiveProfile = profile;
    }

    destroy() {
        if (this._proxy && this._id)
            this._proxy.disconnect(this._id);
        this._proxy = null;
    }
}

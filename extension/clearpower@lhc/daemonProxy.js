import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import {EventEmitter} from 'resource:///org/gnome/shell/misc/signals.js';

export const BUS_NAME = 'org.clearpower.Daemon1';
export const OBJECT_PATH = '/org/clearpower/Daemon';

const IFACE_XML = `
<node>
  <interface name="org.clearpower.Daemon1">
    <property name="Version" type="s" access="read"/>
    <property name="Snapshot" type="a{sv}" access="read"/>
    <property name="ChargeMode" type="s" access="read"/>
    <property name="ChargeLimit" type="i" access="read"/>
    <property name="ChargeTarget" type="i" access="read"/>
    <property name="ChargeControlSupported" type="b" access="read"/>
    <property name="DischargeSupported" type="b" access="read"/>
    <method name="SetChargeLimit"><arg name="percent" type="i" direction="in"/></method>
    <method name="StartTopUp"/>
    <method name="StartDischarge"><arg name="target_percent" type="i" direction="in"/></method>
    <method name="CancelSpecial"/>
    <method name="GetTopProcesses">
      <arg name="count" type="i" direction="in"/><arg name="procs" type="a(sdd)" direction="out"/>
    </method>
    <method name="GetHistory">
      <arg name="field" type="s" direction="in"/><arg name="seconds" type="i" direction="in"/>
      <arg name="points" type="a(dd)" direction="out"/>
    </method>
    <signal name="Sample"><arg name="snapshot" type="a{sv}"/></signal>
  </interface>
</node>`;

const DaemonProxy = Gio.DBusProxy.makeProxyWrapper(IFACE_XML);

function unpackDict(raw) {
    const out = {};
    for (const k of Object.keys(raw)) {
        const v = raw[k];
        out[k] = v instanceof GLib.Variant ? v.recursiveUnpack() : v;
    }
    return out;
}

/** Thin client around the daemon. Emits 'sample' (snapshot), 'state' and 'online'. */
export class DaemonClient extends EventEmitter {
    constructor(bus = Gio.DBus.system) {
        super();
        this.snapshot = null;
        this.online = false;
        this.mode = 'limit';
        this.limit = 100;
        this.target = 0;
        this.controlSupported = false;
        this.dischargeSupported = false;
        this._proxy = new DaemonProxy(bus, BUS_NAME, OBJECT_PATH, (proxy, error) => {
            if (error) {
                console.error(`ClearPower: proxy init failed: ${error.message}`);
                return;
            }
            this._sigId = proxy.connectSignal('Sample', (_p, _sender, [snap]) => {
                this.snapshot = unpackDict(snap);
                if (!this.online)
                    this._setOnline(true);
                this._syncState();
                this.emit('sample', this.snapshot);
            });
            this._ownerId = proxy.connect('notify::g-name-owner', () => this._syncOwner());
            this._propsId = proxy.connect('g-properties-changed', () => {
                this._syncState();
                this.emit('state');
            });
            this._syncOwner();
        }, null, Gio.DBusProxyFlags.NONE);
    }

    _syncOwner() {
        const owned = !!this._proxy.g_name_owner;
        if (owned) {
            const snap = this._proxy.Snapshot;
            if (snap)
                this.snapshot = unpackDict(snap);
            this._syncState();
        }
        this._setOnline(owned);
    }

    _setOnline(v) {
        if (this.online === v)
            return;
        this.online = v;
        this.emit('online', v);
    }

    _syncState() {
        const p = this._proxy;
        this.mode = p.ChargeMode ?? 'limit';
        this.limit = p.ChargeLimit ?? 100;
        this.target = p.ChargeTarget ?? 0;
        this.controlSupported = p.ChargeControlSupported ?? false;
        this.dischargeSupported = p.DischargeSupported ?? false;
    }

    async setChargeLimit(pct) {
        await this._proxy.SetChargeLimitAsync(pct);
    }

    async startTopUp() {
        await this._proxy.StartTopUpAsync();
    }

    async startDischarge(target = 0) {
        await this._proxy.StartDischargeAsync(target);
    }

    async cancelSpecial() {
        await this._proxy.CancelSpecialAsync();
    }

    destroy() {
        if (this._proxy) {
            if (this._sigId)
                this._proxy.disconnectSignal(this._sigId);
            if (this._ownerId)
                this._proxy.disconnect(this._ownerId);
            if (this._propsId)
                this._proxy.disconnect(this._propsId);
        }
        this._proxy = null;
    }
}

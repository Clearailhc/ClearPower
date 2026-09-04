import Cairo from 'gi://cairo';
import GLib from 'gi://GLib';
import GObject from 'gi://GObject';
import Pango from 'gi://Pango';
import PangoCairo from 'gi://PangoCairo';
import St from 'gi://St';

import {roundRect} from './batteryBar.js';
import {t} from './i18n.js';
import {drawGlyph} from './glyphs.js';

// Soft pastel palette, shared by nodes and bands.
const C = {
    adapter: [0.36, 0.60, 0.92],
    battery: [0.40, 0.78, 0.52],
    pc: [0.58, 0.62, 0.68],
    cpu: [0.60, 0.48, 0.92],
    gpu: [0.86, 0.45, 0.78],
    soc: [0.45, 0.55, 0.95],
    mem: [0.55, 0.62, 0.78],
    disp: [0.92, 0.66, 0.34],
    other: [0.30, 0.74, 0.72],
};

const FPS = 15;            // upper bound while the popover is open
const LERP = 0.18;         // per-frame easing towards the latest sample
const SHEEN_SPEED = 0.28;  // band sheen cycles per second
const MIN_SINK_W = 0.1;    // sinks below this are folded into "other" and hidden
const NODE_H = 46, GAP = 10, PAD = 6;

// Right-hand sinks in display order. `key` is the snapshot field.
const SINKS = [
    {id: 'cpu', key: 'cpu_w', label: 'cpu', color: C.cpu},
    {id: 'gpu', key: 'gpu_w', label: 'gpu', color: C.gpu},
    {id: 'soc', key: 'soc_w', label: 'soc', color: C.soc},
    {id: 'mem', key: 'mem_w', label: 'memory', color: C.mem},
    {id: 'disp', key: 'display_w', label: 'display', color: C.disp, approx: true},
    {id: 'other', key: 'other_w', label: 'other', color: C.other},
];
const NUMERIC = ['sys_w', 'bat_w', 'adapter_w', ...SINKS.map(s => s.key)];

export function fmtW(w, digits = null) {
    if (w == null || !isFinite(w) || w < 0)
        return '–';
    const d = digits ?? (w >= 100 ? 0 : 1);
    return `${w.toFixed(d)} W`;
}

export const Sankey = GObject.registerClass(
class Sankey extends St.DrawingArea {
    _init() {
        super._init({x_expand: true, height: 3 * NODE_H + 2 * GAP + 2 * PAD});
        this._target = null;   // latest snapshot
        this._shown = null;    // eased values actually drawn
        this._phase = 0;
        this._timer = 0;
        this._active = false;
        this._lastTick = 0;
        this._cache = null;    // {key, surface, scale}: node cards + text, re-rendered only when values change
        this._flowMode = 'on-ac';  // always | on-ac | never (user preference)
        this.connect('repaint', () => this._draw());
        this.connect('destroy', () => this._stopTimer());
    }

    /** New data from the daemon. Eases in while visible, snaps otherwise. */
    update(snap) {
        this._target = snap;
        if (!this._shown) {
            this._shown = {...snap};
        } else {
            for (const k of Object.keys(snap)) {
                if (!NUMERIC.includes(k) || (snap[k] ?? -1) < 0)
                    this._shown[k] = snap[k];  // non-numeric and "unknown" (-1) snap instantly
            }
        }
        this._fitHeight();
        if (this._active) {
            this._startTimer();
        } else {
            for (const k of NUMERIC)
                this._shown[k] = snap[k];
            this.queue_repaint();
        }
    }

    /** Language change etc.: drop the cached node layer. */
    invalidate() {
        this._cache = null;
        this.queue_repaint();
    }

    /** Only animate while the popover is open: zero cost when closed. */
    setActive(active) {
        this._active = active;
        if (active)
            this._startTimer();
        else
            this._stopTimer();
    }

    /** Preference: 'always' | 'on-ac' | 'never'. System "reduce animations" always wins. */
    setFlowMode(mode) {
        this._flowMode = mode;
        if (this._active)
            this._startTimer();
        this.queue_repaint();
    }

    _sheenEnabled() {
        if (!St.Settings.get().enable_animations || this._flowMode === 'never')
            return false;
        if (this._flowMode === 'on-ac')
            return !!this._target?.on_ac;
        return true;
    }

    _startTimer() {
        if (this._timer)
            return;
        this._lastTick = GLib.get_monotonic_time();
        this._timer = GLib.timeout_add(GLib.PRIORITY_DEFAULT, Math.round(1000 / FPS), () => this._frame());
    }

    _stopTimer() {
        if (this._timer)
            GLib.source_remove(this._timer);
        this._timer = 0;
    }

    _frame() {
        const now = GLib.get_monotonic_time();
        const dt = Math.min((now - this._lastTick) / 1e6, 0.25);
        this._lastTick = now;
        let moving = false;
        if (this._target && this._shown) {
            for (const k of NUMERIC) {
                const tv = this._target[k] ?? 0;
                if (tv < 0) {
                    this._shown[k] = tv;
                    continue;
                }
                const s = (this._shown[k] ?? tv) < 0 ? tv : this._shown[k];
                const n = s + (tv - s) * LERP;
                if (Math.abs(tv - n) > 0.005) {
                    this._shown[k] = n;
                    moving = true;
                } else {
                    this._shown[k] = tv;
                }
            }
        }
        const sheen = this._sheenEnabled();
        if (sheen)
            this._phase = (this._phase + dt * SHEEN_SPEED) % 1;
        this.queue_repaint();
        if (!sheen && !moving) {
            this._timer = 0;
            return GLib.SOURCE_REMOVE;  // idle until the next sample
        }
        return GLib.SOURCE_CONTINUE;
    }

    /** Which sinks are visible is decided on the *target* values so bands never flicker. */
    _visibleSinks() {
        const tg = this._target;
        if (!tg)
            return [];
        const measured = (tg.cpu_w ?? -1) >= 0;
        const displayKnown = (tg.display_w ?? -1) >= 0;
        if (!measured)  // no RAPL: everything we know is the total
            return [{...SINKS[5], key: 'sys_w', label: 'system'}];
        const vis = SINKS.filter(s => {
            if (s.id === 'disp' && !displayKnown)
                return false;
            return (tg[s.key] ?? -1) >= MIN_SINK_W;
        });
        if (!vis.some(s => s.id === 'other'))
            vis.push(SINKS[5]);  // "other" always exists: it collects the hidden remainder
        return vis.map(s => (s.id === 'other' && !displayKnown) ? {...s, label: 'displayOther'} : s);
    }

    _fitHeight() {
        const n = Math.max(3, this._visibleSinks().length);
        const h = n * NODE_H + (n - 1) * GAP + 2 * PAD;
        if (this.height !== h)
            this.height = h;
    }

    _model(s) {
        const onAc = !!s.on_ac;
        const batW = s.bat_w ?? 0;
        const sysW = Math.max(s.sys_w ?? 0, 0);
        const cols = [[], [], []];
        const nodes = {};
        const flows = [];
        const add = (col, id, label, w, color) => {
            const n = {id, label, w, color, inTot: 0, outTot: 0, inOff: 0, outOff: 0};
            nodes[id] = n;
            cols[col].push(n);
            return n;
        };
        const flow = (a, b, w) => {
            if (w > 0.005)
                flows.push({a, b, w});
        };
        if (onAc) {
            const fromBat = Math.max(-batW, 0);
            const toBat = Math.max(batW, 0);
            const adToPc = Math.max(sysW - fromBat, 0);
            add(0, 'adapter', t('adapter'), adToPc + toBat, C.adapter);
            if (fromBat > 0)
                add(0, 'battery', t('battery'), fromBat, C.battery);
            if (toBat > 0)
                add(1, 'batchg', t('battery'), toBat, C.battery);
            add(1, 'pc', t('system'), sysW, C.pc);
            flow('adapter', 'batchg', toBat);
            flow('adapter', 'pc', adToPc);
            flow('battery', 'pc', fromBat);
        } else {
            add(0, 'battery', t('battery'), sysW, C.battery);
            add(1, 'pc', t('system'), sysW, C.pc);
            flow('battery', 'pc', sysW);
        }

        // Sinks: eased values, hidden ones folded into "other", then normalised so
        // that they add up exactly to the eased total.
        const vis = this._visibleSinks();
        const visIds = new Set(vis.map(v => v.id));
        let hidden = 0;
        for (const sk of SINKS) {
            if (!visIds.has(sk.id) && (s[sk.key] ?? -1) > 0 && (this._target?.cpu_w ?? -1) >= 0)
                hidden += s[sk.key];
        }
        const vals = vis.map(v => Math.max(s[v.key] ?? 0, 0) + (v.id === 'other' ? hidden : 0));
        const sum = vals.reduce((a, b) => a + b, 0);
        const k = sum > 0.01 && sysW > 0 ? sysW / sum : 1;
        vis.forEach((v, i) => {
            const n = add(2, v.id, t(v.label), vals[i] * k, v.color);
            n.approx = !!v.approx;
            flow('pc', v.id, vals[i] * k);
        });
        for (const f of flows) {
            nodes[f.a].outTot += f.w;
            nodes[f.b].inTot += f.w;
        }
        return {cols, nodes, flows};
    }

    _layout(m, W, H) {
        const colW = [64, 64, 78];
        const colX = [PAD, Math.round(W / 2 - colW[1] / 2), W - PAD - colW[2]];
        let scale = Infinity;
        for (const col of m.cols) {
            const tot = col.reduce((a, n) => a + n.w, 0);
            const avail = H - 2 * PAD - GAP * (col.length - 1);
            if (tot > 0)
                scale = Math.min(scale, avail / tot);
        }
        if (!isFinite(scale))
            scale = 1;
        for (let pass = 0; pass < 6; pass++) {
            let ok = true;
            for (const col of m.cols) {
                const avail = H - 2 * PAD - GAP * (col.length - 1);
                const need = col.reduce((a, n) => a + Math.max(NODE_H, n.w * scale), 0);
                if (need > avail + 0.5) {
                    scale *= avail / need;
                    ok = false;
                }
            }
            if (ok)
                break;
        }
        m.cols.forEach((col, ci) => {
            const hs = col.map(n => Math.max(NODE_H, n.w * scale));
            const total = hs.reduce((a, b) => a + b, 0) + GAP * (col.length - 1);
            let y = (H - total) / 2;
            col.forEach((n, i) => {
                n.x = colX[ci]; n.y = y; n.w_px = colW[ci]; n.h = hs[i];
                n.inOff = (n.h - n.inTot * scale) / 2;
                n.outOff = (n.h - n.outTot * scale) / 2;
                y += hs[i] + GAP;
            });
        });
        return scale;
    }

    _traceBand(cr, b) {
        const mx = (b.x0 + b.x1) / 2;
        cr.moveTo(b.x0, b.y0);
        cr.curveTo(mx, b.y0, mx, b.y1, b.x1, b.y1);
        cr.lineTo(b.x1, b.y1 + b.t);
        cr.curveTo(mx, b.y1 + b.t, mx, b.y0 + b.t, b.x0, b.y0 + b.t);
        cr.closePath();
    }

    _draw() {
        const cr = this.get_context();
        const [W, H] = this.get_surface_size();
        const node = this.get_theme_node();
        const fg = node.get_foreground_color();
        const s = this._shown;
        if (!s || W < 100) {
            cr.$dispose();
            return;
        }
        const m = this._model(s);
        const scale = this._layout(m, W, H);

        // Bands: soft two-colour gradient plus (optionally) a slow travelling sheen.
        const sheen = this._active && this._sheenEnabled();
        for (const f of m.flows) {
            const a = m.nodes[f.a], b = m.nodes[f.b];
            const band = {
                x0: a.x + a.w_px, y0: a.y + a.outOff,
                x1: b.x, y1: b.y + b.inOff,
                t: Math.max(f.w * scale, 2),
            };
            a.outOff += band.t;
            b.inOff += band.t;
            this._traceBand(cr, band);
            const g = new Cairo.LinearGradient(band.x0, 0, band.x1, 0);
            g.addColorStopRGBA(0, ...a.color, 0.30);
            g.addColorStopRGBA(1, ...b.color, 0.30);
            cr.setSource(g);
            cr.fill();
            if (sheen) {
                const pos = -0.3 + this._phase * 1.6;
                const half = 0.3;
                const tri = x => Math.max(0, 1 - Math.abs(x - pos) / half);
                const stops = [0, pos - half, pos, pos + half, 1]
                    .map(x => Math.min(1, Math.max(0, x)))
                    .sort((p, q) => p - q);
                const sg = new Cairo.LinearGradient(band.x0, 0, band.x1, 0);
                for (const x of stops)
                    sg.addColorStopRGBA(x, 1, 1, 1, 0.16 * tri(x));
                this._traceBand(cr, band);
                cr.setSource(sg);
                cr.fill();
            }
        }

        this._paintNodeLayer(cr, m, W, H, node, fg);
        cr.$dispose();
    }

    /** Node cards + text change only when values change; the sheen must not pay for Pango each frame. */
    _paintNodeLayer(cr, m, W, H, node, fg) {
        const scale = this.get_resource_scale?.() || 1;
        const key = Object.values(m.nodes).map(n => `${n.id}:${n.label}:${n.x}:${n.y}:${Math.round(n.h)}:${n.w.toFixed(1)}`).join('|') +
            `@${W}x${H}x${scale}:${fg.to_string()}`;
        if (!this._cache || this._cache.key !== key) {
            const surface = new Cairo.ImageSurface(Cairo.Format.ARGB32, Math.ceil(W * scale), Math.ceil(H * scale));
            const c2 = new Cairo.Context(surface);
            c2.scale(scale, scale);
            this._drawNodes(c2, m, node, fg);
            c2.$dispose();
            this._cache = {key, surface, scale};
        }
        cr.save();
        cr.scale(1 / this._cache.scale, 1 / this._cache.scale);
        cr.setSourceSurface(this._cache.surface, 0, 0);
        cr.paint();
        cr.restore();
    }

    _drawNodes(cr, m, node, fg) {
        // Nodes: translucent card, glyph on top, bold watts below (label text only when there is no glyph).
        const font = node.get_font();
        const bold = font.copy();
        bold.set_weight(Pango.Weight.BOLD);
        const layout = PangoCairo.create_layout(cr);
        layout.set_ellipsize(Pango.EllipsizeMode.END);
        for (const n of Object.values(m.nodes)) {
            roundRect(cr, n.x, n.y, n.w_px, n.h, 12);
            cr.setSourceRGBA(...n.color, 0.18);
            cr.fillPreserve();
            cr.setSourceRGBA(...n.color, 0.55);
            cr.setLineWidth(1);
            cr.stroke();
            layout.set_width((n.w_px - 6) * Pango.SCALE);
            layout.set_font_description(bold);
            layout.set_text((n.approx ? '≈' : '') + fmtW(n.w), -1);
            const [bw, bh] = layout.get_pixel_size();
            const glyphId = n.id === 'batchg' ? 'battery' : n.id;
            const GLYPH_H = 18;
            if (n.h >= GLYPH_H + bh + 6) {
                const top = n.y + (n.h - GLYPH_H - bh - 2) / 2;
                cr.setSourceRGBA(...n.color, 1);
                drawGlyph(cr, glyphId, n.x + n.w_px / 2, top + GLYPH_H / 2);
                cr.setSourceColor(fg);
                cr.moveTo(n.x + (n.w_px - bw) / 2, top + GLYPH_H + 2);
                PangoCairo.show_layout(cr, layout);
            } else {
                cr.setSourceColor(fg);
                cr.moveTo(n.x + (n.w_px - bw) / 2, n.y + (n.h - bh) / 2);
                PangoCairo.show_layout(cr, layout);
            }
        }
    }
});
